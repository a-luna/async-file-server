using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using AaronLuna.AsyncSocketServer.FileTransfers;
using AaronLuna.AsyncSocketServer.Requests.RequestTypes;
using AaronLuna.Common.Extensions;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncSocketServer.Requests
{
    public class RequestHandler
    {
        Dictionary<RequestType, Func<byte[], Request>> _decodeRequestFunctions;
        Dictionary<RequestType, Func<Request, Result>> _processRequestFunctions;

        int _requestId;
        readonly SocketSettings _settings;
        readonly ServerInfo _localServerInfo;
        readonly List<Request> _processedRequests;
        readonly List<Request> _inProgressRequests;
        readonly List<Request> _sentRequests;
        readonly List<Request> _failedRequests;
        readonly List<Request> _pendingRequests;
        readonly RequestSender _requestSender;
        RequestSender _transferRequestSender;
        Socket _socket;
        bool _inboundFileTransferInProgress;
        bool _outboundFileTransferInProgress;

        static readonly object LockAllRequests = new object();

        public RequestHandler(ServerInfo localServerInfo, SocketSettings settings)
        {
            _requestId = 1;
            _localServerInfo = localServerInfo;
            _settings = settings;

            _pendingRequests = new List<Request>();
            _inProgressRequests = new List<Request>();
            _processedRequests = new List<Request>();
            _sentRequests = new List<Request>();
            _failedRequests = new List<Request>();

            _requestSender = new RequestSender(localServerInfo, settings);
            _requestSender.EventOccurred += HandleServerEvent;
            _requestSender.SuccessfullySentRequest += HandleSentRequest;

            CreateDecodeRequestFunctionsDictionary();
            CreateProcessRequestFunctionsDictionary();
        }

        public event EventHandler<Request> ReceivedServerInfoRequest;
        public event EventHandler<ServerInfoResponse> ReceivedServerInfoResponse;
        public event EventHandler<FileListRequest> ReceivedFileListRequest;
        public event EventHandler<FileListResponse> ReceivedFileListResponse;
        public event EventHandler<Request> RequestedFolderDoesNotExist;
        public event EventHandler<Request> RequestedFolderIsEmpty;
        public event EventHandler<MessageRequest> ReceivedTextMessage;
        public event EventHandler<SendFileRequest> ReceivedInboundFileTransferRequest;
        public event EventHandler<FileTransferResponse> OutboundFileTransferRejected;
        public event EventHandler<FileTransferResponse> OutboundFileTransferAccepted;
        public event EventHandler<FileTransferResponse> OutboundFileTransferComplete;
        public event EventHandler<GetFileRequest> ReceivedOutboundFileTransferRequest;
        public event EventHandler<FileTransferResponse> RequestedFileDoesNotExist;
        public event EventHandler<FileTransferResponse> OutboundFileTransferStalled;
        public event EventHandler<FileTransferResponse> ReceivedRetryStalledFileTransferRequest;
        public event EventHandler<RetryLimitExceeded> ReceivedRetryLimitExceeded;
        public event EventHandler<Request> ReceivedShutdownServerCommand;
        public event EventHandler<ServerEvent> EventOccurred;
        public event EventHandler<ServerEvent> SocketEventOccurred;
        public event EventHandler<List<byte>> ReceivedFileBytes;
        public event EventHandler<string> ErrorOccurred;

        public List<Request> Requests => DeepCopyAllRequests();

        public override string ToString()
        {
            var totalRequests = _processedRequests.Count + _sentRequests.Count + _failedRequests.Count;

            var errors = _failedRequests.Count > 0
                ? $", {_failedRequests.Count} Error"
                : string.Empty;

            var details = totalRequests > 0
                ? $" ({_processedRequests.Count} Rx, {_sentRequests.Count} Tx{errors})"
                : string.Empty;

            return
                $"[{totalRequests} Requests{details}] ";
        }

        public void RegisterEventHandlers(object registrant)
        {
            if (!(registrant is AsyncServer server)) return;

            server.SocketConnectionAccepted += ReceiveRequest;
            server.SuccessfullyProcessedRequest += HandleSuccessfulyProcessedRequest;
            server.InboundFileTransferInProgress += HandleInboundFileTransferInProgress;
            server.OutboundFileTransferInProgress += HandleOutboundFileTransferInProgress;
        }

        async void ReceiveRequest(object sender, Socket socket)
        {
            var requestReceiver = new RequestReceiver(_settings);
            requestReceiver.EventOccurred += HandleServerEvent;
            requestReceiver.SocketEventOccurred += HandleSocketEvent;
            requestReceiver.ReceivedFileBytes += HandleFileBytesReceived;

            EventOccurred?.Invoke(this,
                new ServerEvent { EventType = EventType.ReceiveRequestFromRemoteServerStarted });

            var receiveRequest = await
                requestReceiver.ReceiveRequestAsync(socket).ConfigureAwait(false);

            if (receiveRequest.Failure)
            {
                ReportError(receiveRequest.Error);
                return;
            }

            var encodedRequest = receiveRequest.Value;

            var newRequest = DecodeRequest(encodedRequest);
            AddNewPendingRequest(newRequest);

            EventOccurred?.Invoke(this,
                new ServerEvent { EventType = EventType.ReceiveRequestFromRemoteServerComplete });

            if (FileTransferInProgress())
            {
                EventOccurred?.Invoke(this, new ServerEvent
                {
                    EventType = EventType.PendingRequestInQueue,
                    ItemsInQueueCount = _pendingRequests.Count
                });

                return;
            }

            _socket = requestReceiver.GetTransferSocket();

            ProcessRequest(newRequest);
        }

        public async Task<Result> SendRequestAsync(Request outboundRequest)
        {
            var sendRequest = await _requestSender.SendAsync(outboundRequest);
            if (sendRequest.Success) return Result.Ok();

            outboundRequest.Status = RequestStatus.Error;
            HandleFailedRequest(outboundRequest);
            ReportError(sendRequest.Error);

            return Result.Fail(sendRequest.Error);
        }

        public async Task<Result> AcceptInboundFileTransferAsync(Request outboundRequest)
        {
            _transferRequestSender = new RequestSender(_localServerInfo, _settings);
            _transferRequestSender.EventOccurred += HandleServerEvent;
            _transferRequestSender.SuccessfullySentRequest += HandleSentRequest;

            var sendRequest = await _transferRequestSender.SendAsync(outboundRequest);
            if (sendRequest.Success) return Result.Ok();

            outboundRequest.Status = RequestStatus.Error;
            HandleFailedRequest(outboundRequest);
            ReportError(sendRequest.Error);

            return Result.Fail(sendRequest.Error);
        }

        public Socket GetSocketForInboundFileTransfer()
        {
            return _transferRequestSender?.GetTransferSocket();
        }

        public void CloseInboundFileTransferSocket()
        {
            _transferRequestSender?.ShutdownSocket();
        }

        public Socket GetSocketForOutboundFileTransfer()
        {
            return _socket;
        }

        Request DecodeRequest(byte[] encodedRequest)
        {
            EventOccurred?.Invoke(this,
                new ServerEvent { EventType = EventType.DetermineRequestTypeStarted });

            var requestType = RequestDecoder.ReadRequestType(encodedRequest);

            var newRequest = _decodeRequestFunctions[requestType].Invoke(encodedRequest);
            newRequest.Status = RequestStatus.Pending;
            newRequest.Direction = TransferDirection.Inbound;

            newRequest.DecodeRequest();
            AssignIdToNewRequest(newRequest);

            EventOccurred?.Invoke(this, new ServerEvent
            {
                EventType = EventType.DetermineRequestTypeComplete,
                RequestType = newRequest.Type
            });

            return newRequest;
        }

        void ProcessRequest(Request newRequest)
        {
            EventOccurred?.Invoke(this, new ServerEvent
            {
                EventType = EventType.ProcessRequestStarted,
                RequestType = newRequest.Type,
                RequestId = newRequest.Id,
                RemoteServerIpAddress = newRequest.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = newRequest.RemoteServerInfo.PortNumber
            });

            newRequest.Status = RequestStatus.InProgress;
            HandleInProgressRequest(newRequest);

            var processRequest = _processRequestFunctions[newRequest.Type].Invoke(newRequest);
            if (processRequest.Success) return;

            newRequest.Status = RequestStatus.Error;
            HandleFailedInProgressRequest(newRequest);

            ReportError(processRequest.Error);
        }

        void ProcessPendingRequests()
        {
            if (_pendingRequests.Count == 0) return;

            EventOccurred?.Invoke(this, new ServerEvent
            {
                EventType = EventType.ProcessRequestBacklogStarted,
                ItemsInQueueCount = _pendingRequests.Count
            });

            var pendingIds = _pendingRequests.Select(r => r.Id).ToList();
            foreach (var id in pendingIds)
            {
                var pendingRequest = _pendingRequests.FirstOrDefault(r => r.Id == id);
                if (pendingRequest == null) continue;

                ProcessRequest(pendingRequest);
            }

            EventOccurred?.Invoke(this, new ServerEvent
            {
                EventType = EventType.ProcessRequestBacklogComplete,
                ItemsInQueueCount = _pendingRequests.Count
            });
        }

        void AssignIdToNewRequest(Request request)
        {
            lock (LockAllRequests)
            {
                request.SetId(_requestId);
                _requestId++;
            }
        }

        void AddNewPendingRequest(Request request)
        {
            lock (LockAllRequests)
            {
                _pendingRequests.Add(request);
            }
        }

        void HandleInProgressRequest(Request request)
        {
            lock (LockAllRequests)
            {
                _pendingRequests.RemoveAll(r => r.Id == request.Id);
                _inProgressRequests.Add(request);
            }
        }

        void HandleSuccessfullyProcessedRequest(Request request)
        {
            lock (LockAllRequests)
            {
                _inProgressRequests.RemoveAll(r => r.Id == request.Id);
                _processedRequests.Add(request);
            }
        }

        void HandleFailedInProgressRequest(Request request)
        {
            lock (LockAllRequests)
            {
                _inProgressRequests.RemoveAll(r => r.Id == request.Id);
                _failedRequests.Add(request);
            }
        }

        void HandleFailedRequest(Request request)
        {
            lock (LockAllRequests)
            {
                _failedRequests.Add(request);
            }
        }

        void HandleSentRequest(object sender, Request sentRequest)
        {
            AssignIdToNewRequest(sentRequest);

            lock (LockAllRequests)
            {
                sentRequest.Status = RequestStatus.Sent;
                _sentRequests.Add(sentRequest);
            }
        }

        void HandleSuccessfulyProcessedRequest(object sender, Request processedRequest)
        {
            //var shutdownSocket = ShutdownSocket(_socket);
            //if (shutdownSocket.Failure)
            //{
            //    ReportError(shutdownSocket.Error);
            //    processedRequest.Status = RequestStatus.Error;
            //    HandleFailedInProgressRequest(processedRequest);

            //    return;
            //}

            ShutdownSocket(_socket);

            processedRequest.Status = RequestStatus.Processed;
            HandleSuccessfullyProcessedRequest(processedRequest);

            EventOccurred?.Invoke(this, new ServerEvent
            {
                EventType = EventType.ProcessRequestComplete,
                RequestType = processedRequest.Type,
                RemoteServerIpAddress = processedRequest.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = processedRequest.RemoteServerInfo.PortNumber
            });
        }

        void HandleInboundFileTransferInProgress(object sender, bool inboundFileTransferInProgress)
        {
            _inboundFileTransferInProgress = inboundFileTransferInProgress;

            if (!FileTransferInProgress())
            {
                ProcessPendingRequests();
            }
        }

        void HandleOutboundFileTransferInProgress(object sender, bool outboundFileTransferInProgress)
        {
            _outboundFileTransferInProgress = outboundFileTransferInProgress;

            if (!FileTransferInProgress())
            {
                ProcessPendingRequests();
            }
        }

        bool FileTransferInProgress()
        {
            return _inboundFileTransferInProgress || _outboundFileTransferInProgress;
        }

        Result ShutdownSocket(Socket socket)
        {
            try
            {
                socket.Shutdown(SocketShutdown.Send);
                socket.Close();
            }
            catch (ObjectDisposedException ex)
            {
                return Result.Fail(ex.GetReport());
            }
            catch (SocketException ex)
            {
                return Result.Fail(ex.GetReport());
            }

            return Result.Ok();
        }

        void ReportError(string error)
        {
            ErrorOccurred?.Invoke(this, error);
        }

        void HandleServerEvent(object sender, ServerEvent serverEvent)
        {
            EventOccurred?.Invoke(sender, serverEvent);
        }

        void HandleSocketEvent(object sender, ServerEvent e)
        {
            SocketEventOccurred?.Invoke(sender, e);
        }

        void HandleFileBytesReceived(object sender, List<byte> fileBytes)
        {
            ReceivedFileBytes?.Invoke(sender, fileBytes);
        }

        List<Request> DeepCopyAllRequests()
        {
            var allRequests = new List<Request>();
            allRequests.AddRange(_processedRequests.Select(r => r.Duplicate()));
            allRequests.AddRange(_sentRequests.Select(r => r.Duplicate()));
            allRequests.AddRange(_failedRequests.Select(r => r.Duplicate()));

            return allRequests.OrderBy(r => r.TimeStamp).ToList();
        }

        Result HandleServerInfoRequest(Request pendingRequest)
        {
            ReceivedServerInfoRequest?.Invoke(this, pendingRequest);
            return Result.Ok();
        }

        Result HandleServerInfoResponse(Request pendingRequest)
        {
            if (!(pendingRequest is ServerInfoResponse serverInfoResponse))
            {
                return Result.Fail($"Request received is not valid for this operation ({pendingRequest.Type}).");
            }

            ReceivedServerInfoResponse?.Invoke(this, serverInfoResponse);
            return Result.Ok();
        }

        Result HandleFileListRequest(Request pendingRequest)
        {
            if (!(pendingRequest is FileListRequest fileListRequest))
            {
                return Result.Fail($"Request received is not valid for this operation ({pendingRequest.Type}).");
            }

            ReceivedFileListRequest?.Invoke(this, fileListRequest);
            return Result.Ok();
        }

        Result HandleRequestedFolderDoesNotExist(Request pendingRequest)
        {
            RequestedFolderDoesNotExist?.Invoke(this, pendingRequest);
            return Result.Ok();
        }

        Result HandleRequestedFolderIsEmpty(Request pendingRequest)
        {
            RequestedFolderIsEmpty?.Invoke(this, pendingRequest);
            return Result.Ok();
        }

        Result HandleFileListResponse(Request pendingRequest)
        {
            if (!(pendingRequest is FileListResponse fileListResponse))
            {
                return Result.Fail($"Request received is not valid for this operation ({pendingRequest.Type}).");
            }

            ReceivedFileListResponse?.Invoke(this, fileListResponse);
            return Result.Ok();
        }

        Result HandleTextMessage(Request pendingRequest)
        {
            if (!(pendingRequest is MessageRequest textMessageRequest))
            {
                return Result.Fail($"Request received is not valid for this operation ({pendingRequest.Type}).");
            }

            ReceivedTextMessage?.Invoke(this, textMessageRequest);
            return Result.Ok();
        }

        Result HandleInboundFileTransferRequest(Request pendingRequest)
        {
            if (!(pendingRequest is SendFileRequest sendFileRequest))
            {
                return Result.Fail($"Request received is not valid for this operation ({pendingRequest.Type}).");
            }

            ReceivedInboundFileTransferRequest?.Invoke(this, sendFileRequest);
            return Result.Ok();
        }

        Result HandleFileTransferRejected(Request pendingRequest)
        {
            if (!(pendingRequest is FileTransferResponse fileTransferResponse))
            {
                return Result.Fail($"Request received is not valid for this operation ({pendingRequest.Type}).");
            }

            OutboundFileTransferRejected?.Invoke(this, fileTransferResponse);
            return Result.Ok();
        }

        Result HandleFileTransferAccepted(Request pendingRequest)
        {
            if (!(pendingRequest is FileTransferResponse fileTransferResponse))
            {
                return Result.Fail($"Request received is not valid for this operation ({pendingRequest.Type}).");
            }

            OutboundFileTransferAccepted?.Invoke(this, fileTransferResponse);
            return Result.Ok();
        }

        Result HandleFileTransferComplete(Request pendingRequest)
        {
            if (!(pendingRequest is FileTransferResponse fileTransferResponse))
            {
                return Result.Fail($"Request received is not valid for this operation ({pendingRequest.Type}).");
            }

            OutboundFileTransferComplete?.Invoke(this, fileTransferResponse);
            return Result.Ok();
        }

        Result HandleOutboundFileTransferRequest(Request pendingRequest)
        {
            if (!(pendingRequest is GetFileRequest getFileRequest))
            {
                return Result.Fail($"Request received is not valid for this operation ({pendingRequest.Type}).");
            }

            ReceivedOutboundFileTransferRequest?.Invoke(this, getFileRequest);
            return Result.Ok();
        }

        Result HandleRequestedFileDoesNotExist(Request pendingRequest)
        {
            if (!(pendingRequest is FileTransferResponse fileTransferResponse))
            {
                return Result.Fail($"Request received is not valid for this operation ({pendingRequest.Type}).");
            }

            RequestedFileDoesNotExist?.Invoke(this, fileTransferResponse);
            return Result.Ok();
        }

        Result HandleFileTransferStalled(Request pendingRequest)
        {
            if (!(pendingRequest is FileTransferResponse fileTransferResponse))
            {
                return Result.Fail($"Request received is not valid for this operation ({pendingRequest.Type}).");
            }

            OutboundFileTransferStalled?.Invoke(this, fileTransferResponse);
            return Result.Ok();
        }

        Result HandleRetryOutboundFileTransfer(Request pendingRequest)
        {
            if (!(pendingRequest is FileTransferResponse fileTransferResponse))
            {
                return Result.Fail($"Request received is not valid for this operation ({pendingRequest.Type}).");
            }

            ReceivedRetryStalledFileTransferRequest?.Invoke(this, fileTransferResponse);
            return Result.Ok();
        }

        Result HandleRetryLimitExceeded(Request pendingRequest)
        {
            if (!(pendingRequest is RetryLimitExceeded retryLimitExceeded))
            {
                return Result.Fail($"Request received is not valid for this operation ({pendingRequest.Type}).");
            }

            ReceivedRetryLimitExceeded?.Invoke(this, retryLimitExceeded);
            return Result.Ok();
        }

        Result HandleShutdownServerCommand(Request pendingRequest)
        {
            ReceivedShutdownServerCommand?.Invoke(this, pendingRequest);
            return Result.Ok();
        }

        void CreateDecodeRequestFunctionsDictionary()
        {
            _decodeRequestFunctions =
                new Dictionary<RequestType, Func<byte[], Request>>
            {
                {RequestType.ServerInfoRequest,
                    (encodedrequest) => new Request(encodedrequest)},

                {RequestType.RequestedFolderIsEmpty,
                    (encodedrequest) => new Request(encodedrequest)},

                {RequestType.RequestedFolderDoesNotExist,
                    (encodedrequest) => new Request(encodedrequest)},

                {RequestType.ShutdownServerCommand,
                    (encodedrequest) => new Request(encodedrequest)},

                {RequestType.FileTransferAccepted,
                    (encodedRequest) => new FileTransferResponse(encodedRequest)},

                {RequestType.FileTransferRejected,
                    (encodedRequest) => new FileTransferResponse(encodedRequest)},

                {RequestType.FileTransferStalled,
                    (encodedRequest) => new FileTransferResponse(encodedRequest)},

                {RequestType.FileTransferComplete,
                    (encodedRequest) => new FileTransferResponse(encodedRequest)},

                {RequestType.RetryOutboundFileTransfer,
                    (encodedRequest) => new FileTransferResponse(encodedRequest)},

                {RequestType.MessageRequest,
                    (encodedRequest) => new MessageRequest(encodedRequest)},

                {RequestType.ServerInfoResponse,
                    (encodedRequest) => new ServerInfoResponse(encodedRequest)},

                {RequestType.FileListRequest,
                    (encodedRequest) => new FileListRequest(encodedRequest)},

                {RequestType.FileListResponse,
                    (encodedRequest) => new FileListResponse(encodedRequest)},

                {RequestType.OutboundFileTransferRequest,
                    (encodedRequest) => new GetFileRequest(encodedRequest)},

                {RequestType.InboundFileTransferRequest,
                    (encodedRequest) => new SendFileRequest(encodedRequest)},

                {RequestType.RequestedFileDoesNotExist,
                    (encodedRequest) => new FileTransferResponse(encodedRequest)},

                {RequestType.RetryLimitExceeded,
                    (encodedRequest) => new RetryLimitExceeded(encodedRequest)},
            };
        }

        void CreateProcessRequestFunctionsDictionary()
        {
            _processRequestFunctions = new Dictionary<RequestType, Func<Request, Result>>
            {

                {RequestType.ServerInfoRequest, HandleServerInfoRequest},
                {RequestType.RequestedFolderIsEmpty, HandleRequestedFolderIsEmpty},
                {RequestType.RequestedFolderDoesNotExist, HandleRequestedFolderDoesNotExist},
                {RequestType.ShutdownServerCommand, HandleShutdownServerCommand},
                {RequestType.FileTransferAccepted, HandleFileTransferAccepted},
                {RequestType.FileTransferRejected, HandleFileTransferRejected},
                {RequestType.FileTransferStalled, HandleFileTransferStalled},
                {RequestType.FileTransferComplete, HandleFileTransferComplete},
                {RequestType.RetryOutboundFileTransfer, HandleRetryOutboundFileTransfer},
                {RequestType.MessageRequest, HandleTextMessage},
                {RequestType.ServerInfoResponse, HandleServerInfoResponse},
                {RequestType.FileListRequest, HandleFileListRequest},
                {RequestType.FileListResponse, HandleFileListResponse},
                {RequestType.OutboundFileTransferRequest, HandleOutboundFileTransferRequest},
                {RequestType.InboundFileTransferRequest, HandleInboundFileTransferRequest},
                {RequestType.RequestedFileDoesNotExist, HandleRequestedFileDoesNotExist},
                {RequestType.RetryLimitExceeded, HandleRetryLimitExceeded}
            };
        }
    }
}
