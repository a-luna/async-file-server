using AaronLuna.Common.Extensions;

namespace AaronLuna.AsyncFileServer.Controller
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading.Tasks;

    using Common;
    using Common.Result;

    using Model;
    using Utilities;

    public class RequestController
    {
        internal static readonly string ErrorNoDataReceived =
            "Request from remote server has not been received.";
        
        internal static readonly string ErrorRequestInitiatedByRemoteServer =
            "File Transfer was initiated by remote server, please call GetInboundFileTransfer() " +
            "to retrieve all transfer details.";

        Socket _socket;
        readonly byte[] _buffer;
        readonly int _bufferSize;
        readonly int _timeoutMs;
        int _lastBytesReceivedCount;
        ServerRequest _request;

        IPAddress _remoteServerIpAddress;
        int _remoteServerPortNumber;

        int _remoteServerFileTransferId;
        long _fileTransferResponseCode;
        int _fileTransferRetryCounter;
        int _fileTransferRetryLimit;
        long _fileSizeBytes;
        long _lockoutExpireTimeTicks;
        string _fileName;
        string _localFolderPath;
        string _remoteFolderPath;
        string _textMessage;
        FileInfoList _fileInfoList;

        public RequestController(int id, ServerSettings settings)
        {
            Id = id;
            Status = ServerRequestStatus.NoData;
            RequestType = ServerRequestType.None;
            EventLog = new List<ServerEvent>();
            UnreadBytes = new List<byte>();
            RemoteServerInfo = new ServerInfo();

            _bufferSize = settings.SocketSettings.BufferSize;
            _timeoutMs = settings.SocketSettings.SocketTimeoutInMilliseconds;
            _buffer = new byte[_bufferSize];
        }

        public RequestController(
            int id,
            ServerSettings settings,
            ServerInfo remoteServerInfo) :this(id, settings)
        {
            RemoteServerInfo = remoteServerInfo;
        }

        public RequestController(
            int id,
            ServerSettings settings,
            Socket socket) : this(id, settings)
        {
            _socket = socket;
        }

        public int Id { get; }
        public ServerRequestStatus Status { get; set; }
        public List<ServerEvent> EventLog { get; set; }
        public ServerRequestType RequestType { get; private set; }
        public List<byte> UnreadBytes { get; private set; }
        public ServerInfo RemoteServerInfo { get; set; }
        public int FileTransferId { get; set; }

        public ServerRequestDirection Direction => _request.Direction;
        public DateTime TimeStamp => _request.TimeStamp;
        public bool RequestHasNotBeenReceived => Status == ServerRequestStatus.NoData;
        public bool RequestHasBeenProcessed => Status.RequestHasBeenProcesed();
        public bool IsLongRunningProcess => RequestType.IsLongRunningProcess();
        public bool InboundTransferIsRequested => RequestType == ServerRequestType.InboundFileTransferRequest;
        public bool OutboundTransferIsRequested => RequestType == ServerRequestType.OutboundFileTransferRequest;
        public bool RequestTypeIsFileTransferResponse => RequestType.IsFileTransferResponse();
        public bool RequestTypeIsFileTransferError => RequestType.IsFileTransferError();

        public event EventHandler<ServerEvent> EventOccurred;
        public event EventHandler<ServerEvent> SocketEventOccurred;

        public string ItemText(int itemNumber)
        {
            if (_request == null)
            {
                return string.Empty;
            }

            var space = itemNumber >= 10
                ? string.Empty
                : " ";

            var direction = string.Empty;
            var timeStamp = string.Empty;

            switch (_request.Direction)
            {
                case ServerRequestDirection.Sent:
                    direction = "Sent To........:";
                    timeStamp = "Sent At........:";
                    break;

                case ServerRequestDirection.Received:
                    direction = "Received From..:";
                    timeStamp = "Received At....:";
                    break;
            }

            return $"{space}Request Type...: {RequestType.Name()} [{Status}]{Environment.NewLine}" +
                   $"    {direction} {RemoteServerInfo}{Environment.NewLine}" +
                   $"    {timeStamp} {_request.TimeStamp:MM/dd/yyyy hh:mm tt}{Environment.NewLine}";
        }

        public override string ToString()
        {
            return ItemText(0);
        }

        public async Task<Result> SendServerRequestAsync(
            byte[] requestBytes,
            ServerEvent sendRequestStartedEvent,
            ServerEvent sendRequestCompleteEvent)
        {
            _request = new ServerRequest
            {
                Direction = ServerRequestDirection.Sent,
                RequestBytes = requestBytes
            };

            sendRequestStartedEvent.UpdateTimeStamp();
            EventLog.Add(sendRequestStartedEvent);
            EventOccurred?.Invoke(this, sendRequestStartedEvent);

            ReadRequestBytes(requestBytes);

            var connectToServer =
                await ConnectToServerAsync().ConfigureAwait(false);

            if (connectToServer.Failure)
            {
                return connectToServer;
            }

            var sendRequest =
                await SendRequestBytes(requestBytes).ConfigureAwait(false);

            if (sendRequest.Failure)
            {
                return sendRequest;
            }

            if (RequestType != ServerRequestType.FileTransferAccepted)
            {
                _socket.Shutdown(SocketShutdown.Send);
                _socket.Close();
            }

            sendRequestCompleteEvent.UpdateTimeStamp();
            EventLog.Add(sendRequestCompleteEvent);
            EventOccurred?.Invoke(this, sendRequestCompleteEvent);

            Status = ServerRequestStatus.Sent;

            return Result.Ok();
        }

        async Task<Result> ConnectToServerAsync()
        {
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            EventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ConnectToRemoteServerStarted,
                RemoteServerIpAddress = RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = RemoteServerInfo.PortNumber,
                RequestId = Id,
                FileTransferId = FileTransferId
            });

            EventOccurred?.Invoke(this, EventLog.Last());

            var connectToRemoteServer =
                await _socket.ConnectWithTimeoutAsync(
                    RemoteServerInfo.SessionIpAddress,
                    RemoteServerInfo.PortNumber,
                    _timeoutMs).ConfigureAwait(false);

            if (connectToRemoteServer.Failure)
            {
                return Result.Fail<Socket>(connectToRemoteServer.Error);
            }

            EventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ConnectToRemoteServerComplete,
                RemoteServerIpAddress = RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = RemoteServerInfo.PortNumber,
                RequestId = Id,
                FileTransferId = FileTransferId
            });

            EventOccurred?.Invoke(this, EventLog.Last());

            return Result.Ok();
        }

        async Task<Result> SendRequestBytes(byte[] requestBytes)
        {
            var requestBytesLength = BitConverter.GetBytes(requestBytes.Length);

            var sendRequestLength =
                await _socket.SendWithTimeoutAsync(
                    requestBytesLength,
                    0,
                    requestBytesLength.Length,
                    SocketFlags.None,
                    _timeoutMs).ConfigureAwait(false);

            if (sendRequestLength.Failure)
            {
                return sendRequestLength;
            }

            var sendRequestBytes =
                await _socket.SendWithTimeoutAsync(
                    requestBytes,
                    0,
                    requestBytes.Length,
                    SocketFlags.None,
                    _timeoutMs).ConfigureAwait(false);

            return sendRequestBytes.Success
                ? Result.Ok()
                : sendRequestBytes;
        }

        public async Task<Result> ReceiveServerRequestAsync()
        {
            _request = new ServerRequest {Direction = ServerRequestDirection.Received};
            Status = ServerRequestStatus.NoData;

            EventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceiveRequestFromRemoteServerStarted,
                RequestId = Id,
                FileTransferId = FileTransferId
            });

            EventOccurred?.Invoke(this, EventLog.Last());

            var receiveRequestLength = await ReceiveLengthOfIncomingRequest().ConfigureAwait(false);
            if (receiveRequestLength.Failure)
            {
                return Result.Fail<ServerRequest>(receiveRequestLength.Error);
            }

            var requestLengthInBytes = receiveRequestLength.Value;
            
            var receiveRequestBytes = await ReceiveRequestBytesAsync(requestLengthInBytes).ConfigureAwait(false);
            if (receiveRequestBytes.Failure)
            {
                return Result.Fail<ServerRequest>(receiveRequestBytes.Error);
            }

            _request.RequestBytes = receiveRequestBytes.Value;
            
            EventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.DetermineRequestTypeStarted,
                RequestId = Id,
                FileTransferId = FileTransferId
            });

            EventOccurred?.Invoke(this, EventLog.Last());

            ReadRequestBytes(_request.RequestBytes);

            EventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.DetermineRequestTypeComplete,
                RequestType = RequestType,
                RequestId = Id,
                FileTransferId = FileTransferId
            });

            EventOccurred?.Invoke(this, EventLog.Last());

            EventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceiveRequestFromRemoteServerComplete,
                RemoteServerIpAddress = _remoteServerIpAddress,
                RemoteServerPortNumber = _remoteServerPortNumber,
                RequestType = RequestType,
                RequestId = Id,
                FileTransferId = FileTransferId
            });

            EventOccurred?.Invoke(this, EventLog.Last());

            Status = ServerRequestStatus.Pending;

            return Result.Ok();
        }

        async Task<Result<int>> ReceiveLengthOfIncomingRequest()
        {
            EventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceiveRequestLengthStarted,
                RequestId = Id,
                FileTransferId = FileTransferId
            });

            EventOccurred?.Invoke(this, EventLog.Last());

            var readFromSocket =
                await _socket.ReceiveWithTimeoutAsync(
                        _buffer,
                        0,
                        _bufferSize,
                        SocketFlags.None,
                        _timeoutMs)
                    .ConfigureAwait(false);

            if (readFromSocket.Failure)
            {
                return readFromSocket;
            }

            _lastBytesReceivedCount = readFromSocket.Value;
            var unreadBytesCount = _lastBytesReceivedCount - Constants.SizeOfInt32InBytes;
            var requestLength = BitConverter.ToInt32(_buffer, 0);

            var requestLengthBytes = new byte[Constants.SizeOfInt32InBytes];
            _buffer.ToList().CopyTo(0, requestLengthBytes, 0, Constants.SizeOfInt32InBytes);

            EventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceivedRequestLengthBytesFromSocket,
                BytesReceivedCount = _lastBytesReceivedCount,
                RequestLengthInBytes = Constants.SizeOfInt32InBytes,
                UnreadBytesCount = unreadBytesCount,
                RequestId = Id,
                FileTransferId = FileTransferId
            });

            SocketEventOccurred?.Invoke(this, EventLog.Last());

            if (_lastBytesReceivedCount > Constants.SizeOfInt32InBytes)
            {
                var unreadBytes = new byte[unreadBytesCount];
                _buffer.ToList().CopyTo(Constants.SizeOfInt32InBytes, unreadBytes, 0, unreadBytesCount);
                UnreadBytes = unreadBytes.ToList();

                EventLog.Add(new ServerEvent
                {
                    EventType = ServerEventType.SaveUnreadBytesAfterRequestLengthReceived,
                    UnreadBytesCount = unreadBytesCount,
                    RequestId = Id,
                    FileTransferId = FileTransferId
                });

                EventOccurred?.Invoke(this, EventLog.Last());
            }

            EventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceiveRequestLengthComplete,
                RequestLengthInBytes = requestLength,
                RequestLengthBytes = requestLengthBytes,
                RequestId = Id,
                FileTransferId = FileTransferId
            });

            EventOccurred?.Invoke(this, EventLog.Last());

            return Result.Ok(requestLength);
        }

        async Task<Result<byte[]>> ReceiveRequestBytesAsync(int requestLengthInBytes)
        {
            EventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceiveRequestBytesStarted,
                RequestId = Id,
                FileTransferId = FileTransferId
            });
            EventOccurred?.Invoke(this, EventLog.Last());

            int currentRequestBytesReceived;
            var socketReadCount = 0;
            var bytesReceived = 0;
            var bytesRemaining = requestLengthInBytes;
            var requestBytes = new List<byte>();
            var unreadByteCount = 0;

            if (UnreadBytes.Count > 0)
            {
                currentRequestBytesReceived = Math.Min(requestLengthInBytes, UnreadBytes.Count);
                var unreadRequestBytes = new byte[currentRequestBytesReceived];

                UnreadBytes.CopyTo(0, unreadRequestBytes, 0, currentRequestBytesReceived);
                requestBytes.AddRange(unreadRequestBytes.ToList());

                bytesReceived += currentRequestBytesReceived;
                bytesRemaining -= currentRequestBytesReceived;

                EventLog.Add(new ServerEvent
                {
                    EventType = ServerEventType.CopySavedBytesToRequestData,
                    UnreadBytesCount = UnreadBytes.Count,
                    RequestLengthInBytes = requestLengthInBytes,
                    RequestBytesRemaining = bytesRemaining,
                    RequestId = Id,
                    FileTransferId = FileTransferId
                });

                EventOccurred?.Invoke(this, EventLog.Last());

                if (UnreadBytes.Count > requestLengthInBytes)
                {
                    var fileByteCount = UnreadBytes.Count - requestLengthInBytes;
                    var fileBytes = new byte[fileByteCount];
                    UnreadBytes.CopyTo(requestLengthInBytes, fileBytes, 0, fileByteCount);
                    UnreadBytes = fileBytes.ToList();
                }
                else
                {
                    UnreadBytes = new List<byte>();
                }
            }

            currentRequestBytesReceived = 0;
            while (bytesRemaining > 0)
            {
                var readFromSocket =
                    await _socket.ReceiveWithTimeoutAsync(
                            _buffer,
                            0,
                            _bufferSize,
                            SocketFlags.None,
                            _timeoutMs)
                        .ConfigureAwait(false);

                if (readFromSocket.Failure)
                {
                    return Result.Fail<byte[]>(readFromSocket.Error);
                }

                _lastBytesReceivedCount = readFromSocket.Value;
                currentRequestBytesReceived = Math.Min(bytesRemaining, _lastBytesReceivedCount);

                var requestBytesFromSocket = new byte[currentRequestBytesReceived];
                _buffer.ToList().CopyTo(0, requestBytesFromSocket, 0, currentRequestBytesReceived);
                requestBytes.AddRange(requestBytesFromSocket.ToList());

                socketReadCount++;
                unreadByteCount = _lastBytesReceivedCount - currentRequestBytesReceived;
                bytesReceived += currentRequestBytesReceived;
                bytesRemaining -= currentRequestBytesReceived;

                EventLog.Add(new ServerEvent
                {
                    EventType = ServerEventType.ReceivedRequestBytesFromSocket,
                    SocketReadCount = socketReadCount,
                    BytesReceivedCount = _lastBytesReceivedCount,
                    CurrentRequestBytesReceived = currentRequestBytesReceived,
                    TotalRequestBytesReceived = bytesReceived,
                    RequestLengthInBytes = requestLengthInBytes,
                    RequestBytesRemaining = bytesRemaining,
                    UnreadBytesCount = unreadByteCount,
                    RequestId = Id,
                    FileTransferId = FileTransferId
                });

                SocketEventOccurred?.Invoke(this, EventLog.Last());
            }
            
            EventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceiveRequestBytesComplete,
                RequestBytes = requestBytes.ToArray(),
                RequestId = Id,
                FileTransferId = FileTransferId
            });

            EventOccurred?.Invoke(this, EventLog.Last());

            if (unreadByteCount == 0) return Result.Ok(requestBytes.ToArray());

            var unreadBytes = new byte[unreadByteCount];
            _buffer.ToList().CopyTo(currentRequestBytesReceived, unreadBytes, 0, unreadByteCount);
            UnreadBytes = unreadBytes.ToList();

            EventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.SaveUnreadBytesAfterAllRequestBytesReceived,
                ExpectedByteCount = currentRequestBytesReceived,
                UnreadBytesCount = unreadByteCount,
                RequestId = Id,
                FileTransferId = FileTransferId
            });

            EventOccurred?.Invoke(this, EventLog.Last());

            return Result.Ok(requestBytes.ToArray());
        }

        void ReadRequestBytes(byte[] requestBytes)
        {
            if (_request.Direction == ServerRequestDirection.Received)
            {
                RemoteServerInfo = ServerRequestDataReader.ReadRemoteServerInfo(requestBytes);
            }
            
            RequestType = ServerRequestDataReader.ReadRequestType(requestBytes);

            (_remoteServerIpAddress,
                _,
                _,
                _remoteServerPortNumber,
                _,
                _textMessage,
                _fileInfoList,
                _fileName,
                _fileSizeBytes,
                _localFolderPath,
                _remoteFolderPath,
                _fileTransferResponseCode,
                _remoteServerFileTransferId,
                _fileTransferRetryCounter,
                _fileTransferRetryLimit,
                _lockoutExpireTimeTicks) = ServerRequestDataReader.ReadRequestBytes(requestBytes);
        }

        public Result<TextMessage> GetTextMessage()
        {
            if (RequestHasNotBeenReceived)
            {
                return Result.Fail<TextMessage>(ErrorNoDataReceived);
            }

            var textMessage = new TextMessage
            {
                TimeStamp = _request.TimeStamp,
                Author = TextMessageAuthor.RemoteServer,
                Message = _textMessage,
                Unread = true
            };

            return RequestType == ServerRequestType.TextMessage
                ? Result.Ok(textMessage)
                : Result.Fail<TextMessage>($"Request received is not valid for this operation ({RequestType}).");
        }

        public Result<FileTransferController> GetInboundFileTransfer(
            int fileTransferId,
            ServerInfo localServerInfo,
            ServerSettings settings)
        {
            if (RequestHasNotBeenReceived)
            {
                return Result.Fail<FileTransferController>(ErrorNoDataReceived);
            }

            FileTransferId = fileTransferId;

            var inboundFileTransfer =
                new FileTransferController(fileTransferId, settings)
            {
                RequestId = Id,
                TransferResponseCode = _fileTransferResponseCode,
                RemoteServerRetryLimit = _fileTransferRetryLimit
            };

            inboundFileTransfer.Initialize(
                FileTransferDirection.Inbound,
                FileTransferInitiator.RemoteServer,
                localServerInfo,
                RemoteServerInfo,
                _fileName,
                _fileSizeBytes,
                _localFolderPath,
                _remoteFolderPath);
            
            return RequestType == ServerRequestType.InboundFileTransferRequest
                ? Result.Ok(inboundFileTransfer)
                : Result.Fail<FileTransferController>($"Request received is not valid for this operation ({RequestType}).");
        }

        public Result<bool> RequestedFileExists()
        {
            if (RequestHasNotBeenReceived)
            {
                return Result.Fail<bool>(ErrorNoDataReceived);
            }

            var localFilePath = Path.Combine(_localFolderPath, _fileName);

            return RequestType == ServerRequestType.OutboundFileTransferRequest
                ? Result.Ok(File.Exists(localFilePath))
                : Result.Fail<bool>(
                    $"Request received is not valid for this operation ({RequestType}).");
        }

        public Result<FileTransferController> GetOutboundFileTransfer(
            int fileTransferId,
            ServerSettings settings,
            ServerInfo localServerInfo)
        {
            if (RequestHasNotBeenReceived)
            {
                return Result.Fail<FileTransferController>(ErrorNoDataReceived);
            }

            FileTransferId = fileTransferId;

            var fileTransferController =
                new FileTransferController(fileTransferId, settings)
                {
                    RemoteServerTransferId = _remoteServerFileTransferId
                };

            var localFilePath = Path.Combine(_localFolderPath, _fileName);
            var fileSizeInBytes = new FileInfo(localFilePath).Length;

            fileTransferController.Initialize(
                FileTransferDirection.Outbound,
                FileTransferInitiator.RemoteServer,
                localServerInfo,
                RemoteServerInfo,
                _fileName,
                fileSizeInBytes,
                _localFolderPath,
                _remoteFolderPath);

            return RequestType == ServerRequestType.OutboundFileTransferRequest
                ? Result.Ok(fileTransferController)
                : Result.Fail<FileTransferController>(
                    $"Request received is not valid for this operation ({RequestType}).");
        }

        public Result<long> GetFileTransferResponseCode()
        {
            if (RequestHasNotBeenReceived)
            {
                return Result.Fail<long>(ErrorNoDataReceived);
            }

            return RequestTypeIsFileTransferResponse
                ? Result.Ok(_fileTransferResponseCode)
                : Result.Fail<long>($"Request received is not valid for this operation ({RequestType}).");
        }

        public Result<int> GetRemoteServerFileTransferId()
        {
            if (RequestHasNotBeenReceived)
            {
                return Result.Fail<int>(ErrorNoDataReceived);
            }

            return RequestTypeIsFileTransferError || OutboundTransferIsRequested
                ? Result.Ok(_remoteServerFileTransferId)
                : Result.Fail<int>($"Request received is not valid for this operation ({RequestType}).");
        }

        public Result<int> GetInboundFileTransferId()
        {
            if (RequestHasNotBeenReceived)
            {
                return Result.Fail<int>(ErrorNoDataReceived);
            }

            if (RequestType != ServerRequestType.InboundFileTransferRequest)
            {
                return Result.Fail<int>($"Request received is not valid for this operation ({RequestType}).");
            }
            
            return _remoteServerFileTransferId != 0
                ? Result.Ok(_remoteServerFileTransferId)
                : Result.Fail<int>(ErrorRequestInitiatedByRemoteServer);
        }

        public Result<FileTransferController> UpdateInboundFileTransfer(FileTransferController fileTransfer)
        {
            if (RequestHasNotBeenReceived)
            {
                return Result.Fail<FileTransferController>(ErrorNoDataReceived);
            }

            return RequestType == ServerRequestType.InboundFileTransferRequest
                ? Result.Ok(UpdateTransferDetails(fileTransfer))
                : Result.Fail<FileTransferController>(
                    $"Request received is not valid for this operation ({RequestType}).");
        }

        FileTransferController UpdateTransferDetails(FileTransferController fileTransfer)
        {
            FileTransferId = fileTransfer.Id;

            fileTransfer.RequestId = Id;
            fileTransfer.TransferResponseCode = _fileTransferResponseCode;
            fileTransfer.FileSizeInBytes = _fileSizeBytes;
            fileTransfer.RemoteServerRetryLimit = _fileTransferRetryLimit;

            if (_fileTransferRetryCounter == 1) return fileTransfer;

            fileTransfer.RetryCounter = _fileTransferRetryCounter;
            fileTransfer.Status = FileTransferStatus.Pending;
            fileTransfer.ErrorMessage = string.Empty;

            fileTransfer.RequestInitiatedTime = DateTime.Now;
            fileTransfer.TransferStartTime = DateTime.MinValue;
            fileTransfer.TransferCompleteTime = DateTime.MinValue;

            fileTransfer.CurrentBytesReceived = 0;
            fileTransfer.TotalBytesReceived = 0;
            fileTransfer.BytesRemaining = 0;
            fileTransfer.PercentComplete = 0;

            return fileTransfer;
        }

        public Result<Socket> GetTransferSocket()
        {
            if (RequestHasNotBeenReceived)
            {
                return Result.Fail<Socket>(ErrorNoDataReceived);
            }

            return RequestType == ServerRequestType.FileTransferAccepted
                ? Result.Ok(_socket)
                : Result.Fail<Socket>($"Request received is not valid for this operation ({RequestType}).");
        }

        public Result<FileTransferController>  GetRetryLockoutDetails(FileTransferController fileTransfer)
        {
            if (RequestHasNotBeenReceived)
            {
                return Result.Fail<FileTransferController>(ErrorNoDataReceived);
            }

            return RequestType == ServerRequestType.RetryLimitExceeded
                ? Result.Ok(ApplyRetryLockoutDetails(fileTransfer))
                : Result.Fail<FileTransferController>($"Request received is not valid for this operation ({RequestType}).");
        }

        FileTransferController ApplyRetryLockoutDetails(FileTransferController fileTransfer)
        {
            fileTransfer.RetryLockoutExpireTime = new DateTime(_lockoutExpireTimeTicks);

            return fileTransfer;
        }

        public Result<string> GetLocalFolderPath()
        {
            if (RequestHasNotBeenReceived)
            {
                return Result.Fail<string>(ErrorNoDataReceived);
            }

            return RequestType == ServerRequestType.FileListRequest
                ? Result.Ok(_localFolderPath)
                : Result.Fail<string>($"Request received is not valid for this operation ({RequestType}).");
        }

        public Result<FileInfoList> GetFileInfoList()
        {
            if (RequestHasNotBeenReceived)
            {
                return Result.Fail<FileInfoList>(ErrorNoDataReceived);
            }

            return RequestType == ServerRequestType.FileListResponse
                ? Result.Ok(_fileInfoList)
                : Result.Fail<FileInfoList>($"Request received is not valid for this operation ({RequestType}).");
        }

        public Result ShutdownSocket()
        {
            try
            {
                _socket.Shutdown(SocketShutdown.Send);
                _socket.Close();
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
    }
}
