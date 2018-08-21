using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using AaronLuna.AsyncSocketServer.FileTransfers;
using AaronLuna.AsyncSocketServer.Messaging;
using AaronLuna.AsyncSocketServer.Requests;
using AaronLuna.AsyncSocketServer.Requests.RequestTypes;
using AaronLuna.Common.IO;
using AaronLuna.Common.Logging;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncSocketServer
{
    public class AsyncServer
    {
        //readonly Logger _log = new Logger(typeof(AsyncFileServer));
        readonly List<ServerEvent> _eventLog;
        CancellationToken _token;
        List<byte> _fileBytes;

        readonly ConnectionHandler _connectionHandler;
        readonly RequestHandler _requestHandler;
        readonly MessageHandler _messageHandler;

        public AsyncServer(ServerSettings settings)
        {
            Settings = settings;
            ErrorLog = new List<ServerError>();

            FileTransferHandler = new FileTransferHandler(settings);
            FileTransferHandler.EventOccurred += HandleServerEvent;
            FileTransferHandler.SocketEventOccurred += HandleSocketEvent;
            FileTransferHandler.FileTransferProgress += HandleFileTransferProgress;
            FileTransferHandler.ErrorOccurred += HandleErrorOccurred;
            FileTransferHandler.PendingFileTransfer += HandlePendingFileTransfer;
            FileTransferHandler.InboundFileAlreadyExists += SendNotificationFileAlreadyExists;
            FileTransferHandler.InboundFileTransferComplete += HandleInboundFileTransferComplete;
            FileTransferHandler.RequestedFileDoesNotExist += SendNotificationRequestedFileDoesNotExist;
            FileTransferHandler.ReceivedRetryOutboundFileTransferRequest += ReceivedRetryOutboundFileTransferRequest;
            FileTransferHandler.RetryLimitLockoutExpired += HandleRetryLimitLockoutExpired;

            _connectionHandler = new ConnectionHandler(settings);
            _connectionHandler.RegisterEventHandlers(this);
            _connectionHandler.EventOccurred += HandleServerEvent;
            _connectionHandler.ErrorOccurred += HandleErrorOccurred;
            _connectionHandler.AcceptedSocketConnection += HandleNewRequest;

            _requestHandler = new RequestHandler(MyInfo, Settings.SocketSettings);
            _requestHandler.RegisterEventHandlers(this);
            _requestHandler.EventOccurred += HandleServerEvent;
            _requestHandler.SocketEventOccurred += HandleSocketEvent;
            _requestHandler.ReceivedFileBytes += HandleFileBytesReceived;
            _requestHandler.ErrorOccurred += HandleErrorOccurred;
            _requestHandler.ReceivedServerInfoRequest += HandleServerInfoRequest;
            _requestHandler.ReceivedServerInfoResponse += HandleServerInfoResponse;
            _requestHandler.ReceivedFileListRequest += HandleFileListRequest;
            _requestHandler.RequestedFolderDoesNotExist += HandleRequestedFolderDoesNotExist;
            _requestHandler.RequestedFolderIsEmpty += HandleRequestedFolderIsEmpty;
            _requestHandler.ReceivedFileListResponse += HandleFileListResponse;
            _requestHandler.ReceivedTextMessage += ReceivedNewMessage;
            _requestHandler.ReceivedInboundFileTransferRequest += HandleInboundFileTransferRequest;
            _requestHandler.OutboundFileTransferRejected += HandleOutboundFileTransferRejected;
            _requestHandler.OutboundFileTransferAccepted += HandleOutboundFileTransferAccepted;
            _requestHandler.OutboundFileTransferComplete += HandleOutboundFileTransferComplete;
            _requestHandler.ReceivedOutboundFileTransferRequest += HandleOutboundFileTransferRequest;
            _requestHandler.RequestedFileDoesNotExist += HandleRequestedFileDoesNotExist;
            _requestHandler.OutboundFileTransferStalled += HandleOutboundFileTransferStalled;
            _requestHandler.ReceivedRetryStalledFileTransferRequest += HandleRetryOutboundFileTransfer;
            _requestHandler.ReceivedRetryLimitExceeded += HandleRetryLimitExceeded;
            _requestHandler.ReceivedShutdownServerCommand += HandleShutdownServerCommand;

            _messageHandler = new MessageHandler();
            _messageHandler.EventOccurred += HandleServerEvent;

            _eventLog = new List<ServerEvent>();
            _fileBytes = new List<byte>();
        }

        protected ServerSettings Settings;
        protected FileTransferHandler FileTransferHandler;
        public List<ServerError> ErrorLog { get; }

        public bool IsRunning => _connectionHandler.IsRunning;

        public ServerInfo MyInfo => _connectionHandler.MyInfo;
        public List<Request> Requests => _requestHandler.Requests;
        public List<Conversation> Conversations => _messageHandler.Conversations;
        public List<FileTransfer> FileTransfers => FileTransferHandler.FileTransfers;

        public event EventHandler<ServerEvent> EventOccurred;
        public event EventHandler<ServerEvent> SocketEventOccurred;
        public event EventHandler<ServerEvent> FileTransferProgress;

        internal event EventHandler<Socket> SocketConnectionAccepted;
        internal event EventHandler<Request> SuccessfullyProcessedRequest;
        internal event EventHandler<bool> InboundFileTransferInProgress;
        internal event EventHandler<bool> OutboundFileTransferInProgress;
        internal event EventHandler ShutdownInitiated;

        public override string ToString()
        {
            var name = string.IsNullOrEmpty(MyInfo.Name)
                ? "AsyncFileServer"
                : MyInfo.Name;

            var localEndPoint = $"{MyInfo.LocalIpAddress}:{MyInfo.PortNumber}";

            return
                $"{name} [{localEndPoint}] " + _requestHandler +
                FileTransferHandler + _messageHandler;
        }

        public void UpdateSettings(ServerSettings settings)
        {
            Settings = settings;
        }

        public async Task InitializeAsync(string name = "AsyncFileServer")
        {
            await _connectionHandler.InitializeAsync(name);
        }

        public async Task<Result> RunAsync(CancellationToken token)
        {
            if (!_connectionHandler.IsInitialized)
            {
                await _connectionHandler.InitializeAsync();
            }

            _token = token;
            Logger.Start("server.log");

            return await _connectionHandler.RunAsync(_token);
        }

        public async Task<Result> ShutdownAsync()
        {
            if (!IsRunning)
            {
                return Result.Fail("Server is already shutdown");
            }

            var shutdownServer = new Request(RequestType.ShutdownServerCommand)
            {
                RemoteServerInfo = MyInfo,
                Status = RequestStatus.InProgress,
                Direction = TransferDirection.Outbound
            };

            return await _requestHandler.SendRequestAsync(shutdownServer);
        }

        public Result<Conversation> GetConversationById(int id)
        {
            var matches = Conversations.Select(ts => ts).Where(ts => ts.Id == id).ToList();
            if (matches.Count == 0)
            {
                return Result.Fail<Conversation>($"No text session was found with an ID value of {id}");
            }

            return matches.Count == 1
                ? Result.Ok(matches[0])
                : Result.Fail<Conversation>(
                    $"Found {matches.Count} text sessions with the same ID value of {id}");
        }

        public Result<Request> GetRequestById(int id)
        {
            var matches = Requests.Select(r => r).Where(r => r.Id == id).ToList();
            if (matches.Count == 0)
            {
                return Result.Fail<Request>(
                    $"No file transfer was found with an ID value of {id}");
            }

            return matches.Count == 1
                ? Result.Ok(matches[0])
                : Result.Fail<Request>(
                    $"Found {matches.Count} file transfers with the same ID value of {id}");
        }

        public Result<FileTransfer> GetFileTransferById(int id)
        {
            var matches = FileTransfers.Select(t => t).Where(t => t.Id == id).ToList();
            if (matches.Count == 0)
            {
                return Result.Fail<FileTransfer>(
                    $"No file transfer was found with an ID value of {id}");
            }

            return matches.Count == 1
                ? Result.Ok(matches[0])
                : Result.Fail<FileTransfer>(
                    $"Found {matches.Count} file transfers with the same ID value of {id}");
        }

        public Task<Result> RequestServerInfoAsync(
            IPAddress remoteServerIpAddress,
            int remoteServerPort)
        {
            var serverInfoRequest = new Request(RequestType.ServerInfoRequest)
            {
                LocalServerInfo = MyInfo,
                RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort),
                Status = RequestStatus.InProgress,
                Direction = TransferDirection.Outbound
            };

            return _requestHandler.SendRequestAsync(serverInfoRequest);
        }

        public Task<Result> RequestFileListAsync(ServerInfo remoteServerInfo)
        {
            var fileListRequest = new FileListRequest
            {
                LocalServerInfo = MyInfo,
                RemoteServerInfo = remoteServerInfo,
                TransferFolderPath = remoteServerInfo.TransferFolder,
                Status = RequestStatus.InProgress,
                Direction = TransferDirection.Outbound
            };

            return _requestHandler.SendRequestAsync(fileListRequest);
        }

        public async Task<Result> SendTextMessageAsync(ServerInfo remoteServerInfo, string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return Result.Fail("Message is null or empty string.");
            }

            var messageRequest = new MessageRequest
            {
                LocalServerInfo = MyInfo,
                RemoteServerInfo = remoteServerInfo,
                Message = message,
                Status = RequestStatus.InProgress,
                Direction = TransferDirection.Outbound
            };

            var sendTextMessage = await _requestHandler.SendRequestAsync(messageRequest);
            if (sendTextMessage.Success)
            {
                _messageHandler.AddNewSentMessage(messageRequest);
            }

            return sendTextMessage;
        }

        public async Task<Result> SendFileAsync(
            ServerInfo remoteServerInfo,
            string fileName,
            long fileSizeInBytes,
            string localFolderPath,
            string remoteFolderPath)
        {
            var localFilePath = Path.Combine(localFolderPath, fileName);

            if (!File.Exists(localFilePath))
            {
                return Result.Fail("File does not exist: " + localFilePath);
            }

            remoteServerInfo.TransferFolder = remoteFolderPath;

            var fileTransfer = FileTransferHandler.InitializeFileTransfer(
                TransferDirection.Outbound,
                FileTransferInitiator.Self,
                remoteServerInfo,
                fileName,
                fileSizeInBytes,
                localFolderPath,
                remoteFolderPath,
                DateTime.Now.Ticks,
                Settings.TransferRetryLimit,
                0);

            return await SendOutboundFileTransferRequestAsync(fileTransfer);
        }

        public Task<Result> GetFileAsync(
            ServerInfo remoteServerInfo,
            string fileName,
            long fileSizeInBytes,
            string remoteFolderPath,
            string localFolderPath)
        {
            var fileTransfer = FileTransferHandler.InitializeFileTransfer(
                TransferDirection.Inbound,
                FileTransferInitiator.Self,
                remoteServerInfo,
                fileName,
                fileSizeInBytes,
                localFolderPath,
                remoteFolderPath,
                0,
                0,
                0);

            var getFileRequest = new GetFileRequest()
            {
                LocalServerInfo = MyInfo,
                RemoteServerInfo = fileTransfer.RemoteServerInfo,
                FileName = fileTransfer.FileName,
                RemoteFolderPath = fileTransfer.RemoteFolderPath,
                LocalFolderPath = fileTransfer.LocalFolderPath,
                RemoteServerTransferId = fileTransfer.Id,
                Status = RequestStatus.InProgress,
                Direction = TransferDirection.Outbound
            };

            return _requestHandler.SendRequestAsync(getFileRequest);
        }

        public async Task<Result> AcceptInboundFileTransferAsync(FileTransfer fileTransfer)
        {
            var acceptFileTransfer = new FileTransferResponse(RequestType.FileTransferAccepted)
            {
                LocalServerInfo = MyInfo,
                RemoteServerInfo = fileTransfer.RemoteServerInfo,
                TransferResponseCode = fileTransfer.TransferResponseCode,
                RemoteServerTransferId = fileTransfer.Id,
                Status = RequestStatus.InProgress,
                Direction = TransferDirection.Outbound
            };

            InboundFileTransferInProgress?.Invoke(this, true);

            var sendRequest = await _requestHandler.AcceptInboundFileTransferAsync(acceptFileTransfer);
            if (sendRequest.Failure)
            {
                InboundFileTransferInProgress?.Invoke(this, false);

                var abortTransfer =
                    FileTransferHandler.AbortInboundFileTransfer(
                        acceptFileTransfer,
                        sendRequest.Error);

                return abortTransfer.Success
                    ? sendRequest
                    : Result.Combine(abortTransfer, sendRequest);
            }

            var socket = _requestHandler.GetSocketForInboundFileTransfer();
            if (socket == null)
            {
                InboundFileTransferInProgress?.Invoke(this, false);
                var error = "Unable to retrieve transfer socket, file transfer must be aborted";

                var abortTransfer =
                    FileTransferHandler.AbortInboundFileTransfer(
                        acceptFileTransfer,
                        error);

                return abortTransfer.Success
                    ? Result.Fail(error)
                    : Result.Combine(abortTransfer, Result.Fail(error));
            }

            var receiveFile = await
                FileTransferHandler.AcceptInboundFileTransferAsync(
                    fileTransfer,
                    socket,
                    _fileBytes.ToArray(),
                    _token);

            if (receiveFile.Failure)
            {
                InboundFileTransferInProgress?.Invoke(this, false);
            }

            return receiveFile;
        }

        public async Task<Result> RejectInboundFileTransferAsync(FileTransfer fileTransfer)
        {
            FileTransferHandler.RejectInboundFileTransfer(fileTransfer);

            var rejectTransfer = new FileTransferResponse(RequestType.FileTransferRejected)
            {
                LocalServerInfo = MyInfo,
                RemoteServerInfo = fileTransfer.RemoteServerInfo,
                TransferResponseCode = fileTransfer.TransferResponseCode,
                RemoteServerTransferId = fileTransfer.Id,
                Status = RequestStatus.InProgress,
                Direction = TransferDirection.Outbound
            };

            return await _requestHandler.SendRequestAsync(rejectTransfer);
        }

        public async Task<Result> SendNotificationFileTransferStalledAsync(int fileTransferId)
        {
            var handleTransferStalled = FileTransferHandler.HandleInboundFileTransferStalled(fileTransferId);
            if (handleTransferStalled.Failure)
            {
                return handleTransferStalled;
            }

            var fileTransfer = handleTransferStalled.Value;

            var notifyTransferStalled = new FileTransferResponse(RequestType.FileTransferStalled)
            {
                LocalServerInfo = MyInfo,
                RemoteServerInfo = fileTransfer.RemoteServerInfo,
                TransferResponseCode = fileTransfer.TransferResponseCode,
                RemoteServerTransferId = fileTransfer.Id,
                Status = RequestStatus.InProgress,
                Direction = TransferDirection.Outbound
            };

            InboundFileTransferInProgress?.Invoke(this, false);

            return await _requestHandler.SendRequestAsync(notifyTransferStalled);
        }

        public async Task<Result> RetryFileTransferAsync(int fileTransferId)
        {
            var getFileTransfer = FileTransferHandler.RetryStalledInboundFileTransfer(fileTransferId);
            if (getFileTransfer.Failure)
            {
                return getFileTransfer;
            }

            var stalledFileTransfer = getFileTransfer.Value;

            var retryTransfer = new FileTransferResponse(RequestType.RetryOutboundFileTransfer)
            {
                LocalServerInfo = MyInfo,
                RemoteServerInfo = stalledFileTransfer.RemoteServerInfo,
                TransferResponseCode = stalledFileTransfer.TransferResponseCode,
                RemoteServerTransferId = stalledFileTransfer.Id,
                Status = RequestStatus.InProgress,
                Direction = TransferDirection.Outbound
            };

            FileHelper.DeleteFileIfAlreadyExists(stalledFileTransfer.LocalFilePath, 5);
            return await _requestHandler.SendRequestAsync(retryTransfer);
        }

        public List<ServerEvent> GetEventLogForFileTransfer(int fileTransferId, LogLevel logLevel)
        {
            var eventLog = new List<ServerEvent>();

            var eventsMatchingFileTransferId =
                _eventLog.Select(e => e)
                    .Where(e => e.FileTransferId == fileTransferId)
                    .ToList();

            eventLog.AddRange(eventsMatchingFileTransferId);

            return eventLog.ApplyFilter(logLevel);
        }

        public List<ServerEvent> GetEventLogForRequest(int requestId)
        {
            var eventLog = new List<ServerEvent>();

            var eventsMatchingRequestId =
                _eventLog.Select(e => e)
                    .Where(e => e.RequestId == requestId)
                    .ToList();

            eventLog.AddRange(eventsMatchingRequestId);

            return eventLog.ApplyFilter(LogLevel.Debug);
        }

        public List<ServerEvent> GetCompleteEventLog(LogLevel logLevel)
        {
            var eventLog = new List<ServerEvent>();
            eventLog.AddRange(_eventLog);

            return eventLog.ApplyFilter(logLevel);
        }

        Task<Result> SendOutboundFileTransferRequestAsync(FileTransfer fileTransfer)
        {
            var sendFileRequest = new SendFileRequest
            {
                LocalServerInfo = MyInfo,
                RemoteServerInfo = fileTransfer.RemoteServerInfo,
                FileName = fileTransfer.FileName,
                FileSizeInBytes = fileTransfer.FileSizeInBytes,
                RemoteFolderPath = fileTransfer.RemoteFolderPath,
                LocalFolderPath = fileTransfer.LocalFolderPath,
                FileTransferResponseCode = fileTransfer.TransferResponseCode,
                RemoteServerTransferId = fileTransfer.RemoteServerTransferId,
                RetryLimit = fileTransfer.RemoteServerRetryLimit,
                RetryCounter = fileTransfer.RetryCounter,
                Status = RequestStatus.InProgress,
                Direction = TransferDirection.Outbound
            };

            return _requestHandler.SendRequestAsync(sendFileRequest);
        }

        protected void HandleErrorOccurred(object sender, string error)
        {
            ReportError(sender, error);
        }

        void ReportError(object sender, string error)
        {
            ErrorLog.Add(new ServerError(error));

            _eventLog.Add(new ServerEvent
            {
                EventType = EventType.ErrorOccurred,
                ErrorMessage = error,
                SenderType = sender.GetType()
            });

            EventOccurred?.Invoke(this, _eventLog.Last());
        }

        void HandleServerEvent(object sender, ServerEvent serverEvent)
        {
            _eventLog.Add(serverEvent);
            EventOccurred?.Invoke(sender, serverEvent);
        }

        void HandleSocketEvent(object sender, ServerEvent serverEvent)
        {
            _eventLog.Add(serverEvent);
            SocketEventOccurred?.Invoke(sender, serverEvent);
        }

        void HandleFileTransferProgress(object sender, ServerEvent serverEvent)
        {
            _eventLog.Add(serverEvent);
            FileTransferProgress?.Invoke(sender, serverEvent);
        }

        void HandleFileBytesReceived(object sender, List<byte> fileBytes)
        {
            _fileBytes = fileBytes;
        }

        void HandleNewRequest(object sender, Socket newSocket)
        {
            SocketConnectionAccepted?.Invoke(this, newSocket);
        }

        async void HandleServerInfoRequest(object sender, Request serverInfoRequest)
        {
            _eventLog.Add(new ServerEvent
            {
                EventType = EventType.ReceivedServerInfoRequest,
                RemoteServerIpAddress = serverInfoRequest.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = serverInfoRequest.RemoteServerInfo.PortNumber
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            var serverInfoResponse = new ServerInfoResponse
            {
                LocalServerInfo = MyInfo,
                RemoteServerInfo = serverInfoRequest.RemoteServerInfo,
                LocalIpAddress = MyInfo.LocalIpAddress,
                PublicIpAddress = MyInfo.PublicIpAddress,
                PortNumber = MyInfo.PortNumber,
                Platform = MyInfo.Platform,
                TransferFolderPath = MyInfo.TransferFolder,
                Status = RequestStatus.InProgress,
                Direction = TransferDirection.Outbound
            };

            var sendRequest = await _requestHandler.SendRequestAsync(serverInfoResponse);
            if (sendRequest.Failure)
            {
                ReportError(this, sendRequest.Error);
                return;
            }

            SuccessfullyProcessedRequest?.Invoke(this, serverInfoResponse);
        }

        void HandleServerInfoResponse(object sender, ServerInfoResponse serverInfoResponse)
        {
            serverInfoResponse.RemoteServerInfo.DetermineSessionIpAddress(Settings.LocalNetworkCidrIp);

            _eventLog.Add(new ServerEvent
            {
                EventType = EventType.ReceivedServerInfo,
                RemoteServerIpAddress = serverInfoResponse.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = serverInfoResponse.RemoteServerInfo.PortNumber,
                RemoteServerPlatform = serverInfoResponse.RemoteServerInfo.Platform,
                RemoteFolder = serverInfoResponse.RemoteServerInfo.TransferFolder,
                LocalIpAddress = serverInfoResponse.RemoteServerInfo.LocalIpAddress,
                PublicIpAddress = serverInfoResponse.RemoteServerInfo.PublicIpAddress
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            SuccessfullyProcessedRequest?.Invoke(this, serverInfoResponse);
        }

        async void HandleFileListRequest(object sender, FileListRequest fileListRequest)
        {
            MyInfo.TransferFolder = fileListRequest.TransferFolderPath;

            _eventLog.Add(new ServerEvent
            {
                EventType = EventType.ReceivedFileListRequest,
                RemoteServerIpAddress = fileListRequest.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = fileListRequest.RemoteServerInfo.PortNumber,
                LocalFolder = MyInfo.TransferFolder
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            if (!Directory.Exists(MyInfo.TransferFolder))
            {
                var foldesDoesNotExist = new Request(RequestType.RequestedFolderDoesNotExist)
                {
                    LocalServerInfo = MyInfo,
                    RemoteServerInfo = fileListRequest.RemoteServerInfo,
                    Status = RequestStatus.InProgress,
                    Direction = TransferDirection.Outbound
                };

                await _requestHandler.SendRequestAsync(foldesDoesNotExist);
                return;
            }

            var fileInfoList = new FileInfoList(MyInfo.TransferFolder);
            if (fileInfoList.Count == 0)
            {
                var folderIsEmpty = new Request(RequestType.RequestedFolderIsEmpty)
                {
                    LocalServerInfo = MyInfo,
                    RemoteServerInfo = fileListRequest.RemoteServerInfo,
                    Status = RequestStatus.InProgress,
                    Direction = TransferDirection.Outbound
                };

                await _requestHandler.SendRequestAsync(folderIsEmpty);
                return;
            }

            var fileListResponse = new FileListResponse
            {
                LocalServerInfo = MyInfo,
                RemoteServerInfo = fileListRequest.RemoteServerInfo,
                FileInfoList = fileInfoList,
                Status = RequestStatus.InProgress,
                Direction = TransferDirection.Outbound
            };

            var sendRequest = await _requestHandler.SendRequestAsync(fileListResponse);
            if (sendRequest.Failure)
            {
                return;
            }

            SuccessfullyProcessedRequest?.Invoke(this, fileListRequest);
        }

        private void HandleRequestedFolderDoesNotExist(object sender, Request requestedFolderDoesNotExist)
        {
            _eventLog.Add(new ServerEvent
            {
                EventType = EventType.ReceivedNotificationFolderDoesNotExist,
                RemoteServerIpAddress = requestedFolderDoesNotExist.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = requestedFolderDoesNotExist.RemoteServerInfo.PortNumber
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            SuccessfullyProcessedRequest?.Invoke(this, requestedFolderDoesNotExist);
        }

        private void HandleRequestedFolderIsEmpty(object sender, Request requestedFolderIsEmpty)
        {
            _eventLog.Add(new ServerEvent
            {
                EventType = EventType.ReceivedNotificationFolderIsEmpty,
                RemoteServerIpAddress = requestedFolderIsEmpty.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = requestedFolderIsEmpty.RemoteServerInfo.PortNumber
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            SuccessfullyProcessedRequest?.Invoke(this, requestedFolderIsEmpty);
        }

        private void HandleFileListResponse(object sender, FileListResponse fileListResponse)
        {
            _eventLog.Add(new ServerEvent
            {
                EventType = EventType.ReceivedFileList,
                LocalIpAddress = MyInfo.LocalIpAddress,
                LocalPortNumber = MyInfo.PortNumber,
                RemoteServerIpAddress = fileListResponse.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = fileListResponse.RemoteServerInfo.PortNumber,
                RemoteFolder = fileListResponse.RemoteServerInfo.TransferFolder,
                RemoteServerFileList = fileListResponse.FileInfoList,
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            SuccessfullyProcessedRequest?.Invoke(this, fileListResponse);
        }

        void ReceivedNewMessage(object sender, MessageRequest messageRequest)
        {
            _messageHandler.AddNewReceivedMessage(messageRequest);

            SuccessfullyProcessedRequest?.Invoke(this, messageRequest);
        }

        void HandleInboundFileTransferRequest(object sender, SendFileRequest sendFileRequest)
        {
            _eventLog.Add(new ServerEvent
            {
                EventType = EventType.ReceivedInboundFileTransferRequest,
                LocalFolder = sendFileRequest.LocalFolderPath,
                FileName = sendFileRequest.FileName,
                FileSizeInBytes = sendFileRequest.FileSizeInBytes,
                RemoteServerIpAddress = sendFileRequest.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = sendFileRequest.RemoteServerInfo.PortNumber
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            var handleTransferRequest =
                FileTransferHandler.HandleInboundFileTransferRequest(sendFileRequest);

            if (handleTransferRequest.Failure)
            {
                return;
            }

            SuccessfullyProcessedRequest?.Invoke(this, sendFileRequest);
        }

        protected async void SendNotificationFileAlreadyExists(object sender, SendFileRequest sendFileRequest)
        {
            var rejectFileTransfer = new FileTransferResponse(RequestType.FileTransferRejected)
            {
                LocalServerInfo = MyInfo,
                RemoteServerInfo = sendFileRequest.RemoteServerInfo,
                TransferResponseCode = sendFileRequest.FileTransferResponseCode,
                RemoteServerTransferId = sendFileRequest.Id,
                Status = RequestStatus.InProgress,
                Direction = TransferDirection.Outbound
            };

            var sendRequest = await _requestHandler.SendRequestAsync(rejectFileTransfer);
            if (sendRequest.Failure)
            {
                return;
            }

            SuccessfullyProcessedRequest?.Invoke(this, sendFileRequest);
        }

        protected void HandlePendingFileTransfer(object sender, int pendingTransferCount)
        {
            _eventLog.Add(new ServerEvent
            {
                EventType = EventType.PendingFileTransfer,
                ItemsInQueueCount = pendingTransferCount
            });

            EventOccurred?.Invoke(this, _eventLog.Last());
        }

        protected async void HandleInboundFileTransferComplete(object sender, FileTransfer inboundFileTransfer)
        {
            _requestHandler.CloseInboundFileTransferSocket();

            await SendNotificationFileTransferComplete(inboundFileTransfer);
        }

        async Task SendNotificationFileTransferComplete(FileTransfer inboundFileTransfer)
        {
            var fileTransferComplete = new FileTransferResponse(RequestType.FileTransferComplete)
            {
                LocalServerInfo = MyInfo,
                RemoteServerInfo = inboundFileTransfer.RemoteServerInfo,
                TransferResponseCode = inboundFileTransfer.TransferResponseCode,
                RemoteServerTransferId = inboundFileTransfer.Id,
                Status = RequestStatus.InProgress,
                Direction = TransferDirection.Outbound
            };

            InboundFileTransferInProgress?.Invoke(this, false);

            await _requestHandler.SendRequestAsync(fileTransferComplete);
        }

        async void HandleOutboundFileTransferRequest(object sender, GetFileRequest getFileRequest)
        {
            // TODO: Create logic to check file transfers that are under lockout and if this request matches remoteserver info + localfilepath, send a new filetranserresponse = rejected_retrylimitexceeded. maybe we should penalize them for trying to subvert our lockout policy?

            _eventLog.Add(new ServerEvent
            {
                EventType = EventType.ReceivedOutboundFileTransferRequest,
                RemoteFolder = getFileRequest.RemoteFolderPath,
                LocalFolder = getFileRequest.LocalFolderPath,
                FileName = getFileRequest.FileName,
                RemoteServerIpAddress = getFileRequest.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = getFileRequest.RemoteServerInfo.PortNumber
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            var handleTransferRequest =
                FileTransferHandler.HandleOutboundFileTransferRequest(getFileRequest);

            if (handleTransferRequest.Failure)
            {
                return;
            }

            var fileTransfer = handleTransferRequest.Value;

            var sendFileRequest = new SendFileRequest
            {
                LocalServerInfo = MyInfo,
                RemoteServerInfo = fileTransfer.RemoteServerInfo,
                FileName = fileTransfer.FileName,
                FileSizeInBytes = fileTransfer.FileSizeInBytes,
                RemoteFolderPath = fileTransfer.RemoteFolderPath,
                LocalFolderPath = fileTransfer.LocalFolderPath,
                FileTransferResponseCode = fileTransfer.TransferResponseCode,
                RemoteServerTransferId = fileTransfer.RemoteServerTransferId,
                RetryLimit = fileTransfer.RemoteServerRetryLimit,
                RetryCounter = fileTransfer.RetryCounter,
                Status = RequestStatus.InProgress,
                Direction = TransferDirection.Outbound
            };

            await _requestHandler.SendRequestAsync(sendFileRequest);

            SuccessfullyProcessedRequest?.Invoke(this, getFileRequest);
        }

        protected async void SendNotificationRequestedFileDoesNotExist(object sender, GetFileRequest getFileRequest)
        {
            var requestedFileDoesNotExist = new FileTransferResponse(RequestType.RequestedFileDoesNotExist)
            {
                LocalServerInfo = MyInfo,
                RemoteServerInfo = getFileRequest.RemoteServerInfo,
                RemoteServerTransferId = getFileRequest.RemoteServerTransferId,
                Status = RequestStatus.InProgress,
                Direction = TransferDirection.Outbound
            };

            await _requestHandler.SendRequestAsync(requestedFileDoesNotExist);

            SuccessfullyProcessedRequest?.Invoke(this, getFileRequest);
        }

        void HandleOutboundFileTransferRejected(object sender, FileTransferResponse fileTransferResponse)
        {
            _eventLog.Add(new ServerEvent
            {
                EventType = EventType.RemoteServerRejectedFileTransfer,
                RemoteServerIpAddress = fileTransferResponse.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = fileTransferResponse.RemoteServerInfo.PortNumber,
                FileTransferId = fileTransferResponse.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            var rejectFile =
                FileTransferHandler.HandleOutboundFileTransferRejected(fileTransferResponse);

            if (rejectFile.Failure)
            {
                return;
            }

            SuccessfullyProcessedRequest?.Invoke(this, fileTransferResponse);
        }

        async void HandleOutboundFileTransferAccepted(object sender, FileTransferResponse fileTransferResponse)
        {
            _eventLog.Add(new ServerEvent
            {
                EventType = EventType.RemoteServerAcceptedFileTransfer,
                RemoteServerIpAddress = fileTransferResponse.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = fileTransferResponse.RemoteServerInfo.PortNumber,
                FileTransferId = fileTransferResponse.RemoteServerTransferId
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            var socket = _requestHandler.GetSocketForOutboundFileTransfer();
            if (socket == null)
            {
                var error = "Unable to retrieve transfer socket, file transfer must be aborted";
                FileTransferHandler.AbortOutboundFileTransfer(fileTransferResponse, error);

                return;
            }

            OutboundFileTransferInProgress?.Invoke(this, true);

            var sendFile = await
                FileTransferHandler.HandleOutboundFileTransferAccepted(
                    fileTransferResponse,
                    socket,
                    _token);

            if (sendFile.Failure)
            {
                OutboundFileTransferInProgress?.Invoke(this, false);
                return;
            }

            OutboundFileTransferInProgress?.Invoke(this, false);
            SuccessfullyProcessedRequest?.Invoke(this, fileTransferResponse);
        }

        void HandleOutboundFileTransferStalled(object sender, FileTransferResponse fileTransferResponse)
        {
            _eventLog.Add(new ServerEvent
            {
                EventType = EventType.FileTransferStalled,
                RemoteServerIpAddress = fileTransferResponse.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = fileTransferResponse.RemoteServerInfo.PortNumber,
                FileTransferId = fileTransferResponse.Id,
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            var cancelFileTransfer =
                FileTransferHandler.HandleOutboundFileTransferStalled(fileTransferResponse);

            if (cancelFileTransfer.Failure)
            {
                return;
            }

            SuccessfullyProcessedRequest?.Invoke(this, fileTransferResponse);
        }

        void HandleOutboundFileTransferComplete(object sender, FileTransferResponse fileTransferResponse)
        {
            _eventLog.Add(new ServerEvent
            {
                EventType = EventType.RemoteServerConfirmedFileTransferCompleted,
                RemoteServerIpAddress = fileTransferResponse.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = fileTransferResponse.RemoteServerInfo.PortNumber,
                FileTransferId = fileTransferResponse.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            var transferComplete =
                FileTransferHandler.HandleOutboundFileTransferComplete(fileTransferResponse);

            if (transferComplete.Failure)
            {
                return;
            }

            SuccessfullyProcessedRequest?.Invoke(this, fileTransferResponse);
        }

        void HandleRequestedFileDoesNotExist(object sender, FileTransferResponse fileTransferResponse)
        {
            _eventLog.Add(new ServerEvent
            {
                EventType = EventType.ReceivedNotificationFileDoesNotExist,
                RemoteServerIpAddress = fileTransferResponse.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = fileTransferResponse.RemoteServerInfo.PortNumber,
                FileTransferId = fileTransferResponse.RemoteServerTransferId
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            var rejectTransfer =
                FileTransferHandler.HandleRequestedFileDoesNotExist(
                    fileTransferResponse.RemoteServerTransferId);

            if (rejectTransfer.Failure)
            {
                return;
            }

            SuccessfullyProcessedRequest?.Invoke(this, fileTransferResponse);
        }

        protected void HandleRetryLimitLockoutExpired(object sender, FileTransfer stalledFileTransfer)
        {
            _eventLog.Add(new ServerEvent
            {
                EventType = EventType.RetryLimitLockoutExpired,
                LocalFolder = stalledFileTransfer.LocalFolderPath,
                FileName = stalledFileTransfer.FileName,
                FileSizeInBytes = stalledFileTransfer.FileSizeInBytes,
                RemoteServerIpAddress = stalledFileTransfer.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = stalledFileTransfer.RemoteServerInfo.PortNumber,
                RetryCounter = stalledFileTransfer.RetryCounter,
                RemoteServerRetryLimit = stalledFileTransfer.RemoteServerRetryLimit,
                RetryLockoutExpireTime = stalledFileTransfer.RetryLockoutExpireTime,
                FileTransferId = stalledFileTransfer.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());
        }

        void HandleRetryOutboundFileTransfer(object sender, FileTransferResponse fileTransferResponse)
        {
            FileTransferHandler.HandleRetryOutboundFileTransfer(fileTransferResponse);
        }

        protected async void ReceivedRetryOutboundFileTransferRequest(object sender, FileTransfer canceledFileTransfer)
        {
            _eventLog.Add(new ServerEvent
            {
                EventType = EventType.ReceivedRetryOutboundFileTransferRequest,
                LocalFolder = Path.GetDirectoryName(canceledFileTransfer.LocalFilePath),
                FileName = Path.GetFileName(canceledFileTransfer.LocalFilePath),
                FileSizeInBytes = new FileInfo(canceledFileTransfer.LocalFilePath).Length,
                RemoteFolder = canceledFileTransfer.RemoteFolderPath,
                RemoteServerIpAddress = canceledFileTransfer.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = canceledFileTransfer.RemoteServerInfo.PortNumber,
                RetryCounter = canceledFileTransfer.RetryCounter,
                RemoteServerRetryLimit = Settings.TransferRetryLimit,
                FileTransferId = canceledFileTransfer.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            if (canceledFileTransfer.Status == FileTransferStatus.RetryLimitExceeded)
            {
                if (canceledFileTransfer.RetryLockoutExpired)
                {
                    canceledFileTransfer.RetryCounter = 0;
                    canceledFileTransfer.ResetTransferValues();

                    _eventLog.Add(new ServerEvent
                    {
                        EventType = EventType.RetryLimitLockoutExpired,
                        LocalFolder = canceledFileTransfer.LocalFolderPath,
                        FileName = canceledFileTransfer.FileName,
                        FileSizeInBytes = canceledFileTransfer.FileSizeInBytes,
                        RemoteServerIpAddress = canceledFileTransfer.RemoteServerInfo.SessionIpAddress,
                        RemoteServerPortNumber = canceledFileTransfer.RemoteServerInfo.PortNumber,
                        RetryCounter = canceledFileTransfer.RetryCounter,
                        RemoteServerRetryLimit = canceledFileTransfer.RemoteServerRetryLimit,
                        RetryLockoutExpireTime = canceledFileTransfer.RetryLockoutExpireTime,
                        FileTransferId = canceledFileTransfer.Id
                    });

                    EventOccurred?.Invoke(this, _eventLog.Last());

                    await SendOutboundFileTransferRequestAsync(canceledFileTransfer);
                    return;
                }

                await SendRetryLimitExceededAsync(canceledFileTransfer);
                return;
            }

            if (canceledFileTransfer.RetryCounter >= Settings.TransferRetryLimit)
            {
                var retryLimitExceeded =
                    "Maximum # of attempts to complete stalled file transfer reached or " +
                    $"exceeded ({Settings.TransferRetryLimit} failed attempts for " +
                    $"\"{canceledFileTransfer.FileName}\"):" +
                    Environment.NewLine + Environment.NewLine + canceledFileTransfer.OutboundRequestDetails();

                canceledFileTransfer.Status = FileTransferStatus.RetryLimitExceeded;
                canceledFileTransfer.RetryLockoutExpireTime = DateTime.Now + Settings.RetryLimitLockout;
                canceledFileTransfer.ErrorMessage = "Maximum # of attempts to complete stalled file transfer reached";
                ReportError(this, retryLimitExceeded);

                await SendRetryLimitExceededAsync(canceledFileTransfer);
                return;
            }

            canceledFileTransfer.ResetTransferValues();

            await SendOutboundFileTransferRequestAsync(canceledFileTransfer);
        }

        async Task SendRetryLimitExceededAsync(FileTransfer fileTransfer)
        {
            var retryLimitExceeded = new RetryLimitExceeded
            {
                LocalServerInfo = MyInfo,
                RemoteServerInfo = fileTransfer.RemoteServerInfo,
                RemoteServerTransferId = fileTransfer.RemoteServerTransferId,
                RetryLimit = Settings.TransferRetryLimit,
                LockoutExpireTime = fileTransfer.RetryLockoutExpireTime,
                Status = RequestStatus.InProgress,
                Direction = TransferDirection.Outbound
            };

            await _requestHandler.SendRequestAsync(retryLimitExceeded);
        }

        void HandleRetryLimitExceeded(object sender, RetryLimitExceeded retryLimitExceeded)
        {
            var getFileTransfer = FileTransferHandler.HandleRetryLimitExceeded(retryLimitExceeded);
            if (getFileTransfer.Failure)
            {
                return;
            }

            var inboundFileTransfer = getFileTransfer.Value;

            _eventLog.Add(new ServerEvent
            {
                EventType = EventType.ReceivedRetryLimitExceeded,
                LocalFolder = inboundFileTransfer.LocalFolderPath,
                FileName = inboundFileTransfer.FileName,
                FileSizeInBytes = inboundFileTransfer.FileSizeInBytes,
                RemoteServerIpAddress = inboundFileTransfer.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = inboundFileTransfer.RemoteServerInfo.PortNumber,
                RetryCounter = inboundFileTransfer.RetryCounter,
                RemoteServerRetryLimit = inboundFileTransfer.RemoteServerRetryLimit,
                RetryLockoutExpireTime = inboundFileTransfer.RetryLockoutExpireTime,
                FileTransferId = inboundFileTransfer.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());
            SuccessfullyProcessedRequest?.Invoke(this, retryLimitExceeded);
        }

        private void HandleShutdownServerCommand(object sender, Request pendingRequest)
        {
            if (!MyInfo.IsEqualTo(pendingRequest.RemoteServerInfo))
            {
                var error =
                    "Server shutdown command was received, but the command was not sent " +
                    "by this server. Aborting shutdown process.";

                ReportError(this, error);
                return;
            }

            _eventLog.Add(new ServerEvent
            {
                EventType = EventType.ReceivedShutdownServerCommand,
                RemoteServerIpAddress = pendingRequest.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = pendingRequest.RemoteServerInfo.PortNumber,
                RequestId = pendingRequest.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());
            ShutdownInitiated?.Invoke(this, new EventArgs());
            SuccessfullyProcessedRequest?.Invoke(this, pendingRequest);
        }

        protected virtual void OnEventOccurred(ServerEvent serverEvent)
        {
            var handler = EventOccurred;
            handler?.Invoke(this, serverEvent);
        }

        protected virtual void OnSocketEventOccurred(ServerEvent serverEvent)
        {
            var handler = SocketEventOccurred;
            handler?.Invoke(this, serverEvent);
        }

        protected virtual void OnFileTransferProgress(ServerEvent serverEvent)
        {
            var handler = FileTransferProgress;
            handler?.Invoke(this, serverEvent);
        }
    }
}
