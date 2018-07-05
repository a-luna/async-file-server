namespace AaronLuna.AsyncFileServer.Controller
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading.Tasks;

    using Model;
    using Utilities;
    using Common;
    using Common.Result;

    public class ServerRequestController
    {
        Socket _socket;
        readonly byte[] _buffer;
        readonly int _bufferSize;
        readonly int _timeoutMs;
        int _lastBytesReceivedCount;
        ServerRequest _request;

        IPAddress _remoteServerIpAddress;
        int _remoteServerPortNumber;
        
        int _fileTransferId;
        int _fileTransferRetryCounter;
        int _fileTransferRetryLimit;
        long _fileSizeBytes;
        long _fileTransferResponseCode;
        long _lockoutExpireTimeTicks;
        string _requestedFilePath;
        string _localFilePath;
        string _localFolderPath;
        string _remoteFolderPath;
        string _textMessage;
        FileInfoList _fileInfoList;
        
        public ServerRequestController(int id, int bufferSize, int timeoutMs)
        {
            Id = id;
            Status = ServerRequestStatus.NoData;
            RequestType = ServerRequestType.None;
            EventLog = new List<ServerEvent>();
            UnreadBytes = new List<byte>();
            RemoteServerInfo = new ServerInfo();
            
            _bufferSize = bufferSize;
            _timeoutMs = timeoutMs;
            _buffer = new byte[bufferSize];
        }

        public int Id { get; }
        public ServerRequestStatus Status { get; set; }
        public List<ServerEvent> EventLog { get; set; }        
        public ServerRequestType RequestType { get; private set; }
        public List<byte> UnreadBytes { get; private set; }
        public ServerInfo RemoteServerInfo { get; private set; }
        public int FileTransferId { get; set; }

        public bool IsInboundFileTransferRequest => RequestType == ServerRequestType.InboundFileTransferRequest;
        public bool RequestTypeIsFileTransferResponse => RequestType.IsFileTransferResponse();
        public bool RequestTypeIsFileTransferError => RequestType.IsFileTransferError();

        public event EventHandler<ServerEvent> EventOccurred;
        public event EventHandler<ServerEvent> SocketEventOccurred;

        public override string ToString()
        {
            if (_request == null)
            {
                return string.Empty;
            }

            var direction = string.Empty;
            if (_request.Direction == ServerRequestDirection.Sent)
            {
                direction = "Sent to:";
            }

            if (_request.Direction == ServerRequestDirection.Received)
            {
                direction = "Received from:";
            }

            return RequestType.Name() + Environment.NewLine +
                   $"{direction} {RemoteServerInfo} at {_request.TimeStamp:MM/dd/yyyy hh:mm tt}";
        }

        public async Task<Result> SendServerRequestAsync(
            byte[] requestBytes,
            IPAddress remoteServerIp,
            int remoteServerPort,
            ServerEvent sendRequestStartedEvent,
            ServerEvent sendRequestCompleteEvent)
        {
            _request = new ServerRequest {Direction = ServerRequestDirection.Sent, RequestBytes = requestBytes};
            EventLog.Add(sendRequestStartedEvent);
            EventOccurred?.Invoke(this, sendRequestStartedEvent);
            ReadRequestBytes(requestBytes);

            var connectToServer =
                await ConnectToServerAsync(remoteServerIp, remoteServerPort).ConfigureAwait(false);

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

            EventLog.Add(sendRequestCompleteEvent);
            EventOccurred?.Invoke(this, sendRequestCompleteEvent);
            Status = ServerRequestStatus.Sent;

            return Result.Ok();
        }

        async Task<Result> ConnectToServerAsync(IPAddress remoteServerIp, int remoteServerPort)
        {
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            EventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ConnectToRemoteServerStarted,
                RemoteServerIpAddress = remoteServerIp,
                RemoteServerPortNumber = remoteServerPort
            });

            EventOccurred?.Invoke(this, EventLog.Last());

            var connectToRemoteServer =
                await _socket.ConnectWithTimeoutAsync(
                    remoteServerIp,
                    remoteServerPort,
                    _timeoutMs).ConfigureAwait(false);

            if (connectToRemoteServer.Failure)
            {
                return Result.Fail<Socket>(connectToRemoteServer.Error);
            }

            EventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ConnectToRemoteServerComplete,
                RemoteServerIpAddress = remoteServerIp,
                RemoteServerPortNumber = remoteServerPort
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
        
        public async Task<Result> ReceiveServerRequestAsync(Socket socket)
        {
            _socket = socket;
            _request = new ServerRequest {Direction = ServerRequestDirection.Received};
            Status = ServerRequestStatus.NoData;

            EventLog.Add(new ServerEvent { EventType = ServerEventType.ReceiveRequestFromRemoteServerStarted });
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
                EventType = ServerEventType.ReceiveRequestFromRemoteServerComplete,
                RemoteServerIpAddress = _remoteServerIpAddress,
                RequestType = RequestType
            });

            EventOccurred?.Invoke(this, EventLog.Last());

            Status = ServerRequestStatus.Pending;

            return Result.Ok();
        }

        async Task<Result<int>> ReceiveLengthOfIncomingRequest()
        {
            EventLog.Add(new ServerEvent { EventType = ServerEventType.ReceiveRequestLengthStarted });
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
                UnreadBytesCount = unreadBytesCount
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
                });

                EventOccurred?.Invoke(this, EventLog.Last());
            }

            EventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceiveRequestLengthComplete,
                RequestLengthInBytes = requestLength,
                RequestLengthBytes = requestLengthBytes
            });

            EventOccurred?.Invoke(this, EventLog.Last());

            return Result.Ok(requestLength);
        }

        async Task<Result<byte[]>> ReceiveRequestBytesAsync(int requestLengthInBytes)
        {
            EventLog.Add(new ServerEvent { EventType = ServerEventType.ReceiveRequestBytesStarted });
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
                    RequestBytesRemaining = bytesRemaining
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
                    UnreadBytesCount = unreadByteCount
                });

                SocketEventOccurred?.Invoke(this, EventLog.Last());
            }

            ReadRequestBytes(requestBytes.ToArray());

            EventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceiveRequestBytesComplete,
                RequestBytes = requestBytes.ToArray()
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
                UnreadBytesCount = unreadByteCount
            });

            EventOccurred?.Invoke(this, EventLog.Last());

            return Result.Ok(requestBytes.ToArray());
        }

        void ReadRequestBytes(byte[] requestBytes)
        {
            RemoteServerInfo = ServerRequestDataReader.ReadRemoteServerInfo(requestBytes);
            RequestType = ServerRequestDataReader.ReadRequestType(requestBytes);
            
            (_remoteServerIpAddress,
                _remoteServerPortNumber,
                _textMessage,
                _fileTransferId,
                _,
                _,
                _requestedFilePath,
                _localFilePath,
                _localFolderPath,
                _remoteFolderPath,
                _fileInfoList,
                _fileSizeBytes,
                _fileTransferResponseCode,
                _lockoutExpireTimeTicks,
                _fileTransferRetryCounter,
                _fileTransferRetryLimit) = ServerRequestDataReader.ReadRequestBytes(requestBytes);            
        }

        public Result<TextMessage> GetTextMessage()
        {
            if (Status == ServerRequestStatus.NoData)
            {
                return Result.Fail<TextMessage>("Request from remote server has not been received.");
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

        public Result<FileTransfer> GetInboundFileTransfer(ServerInfo localServerInfo, int fileTransferId)
        {
            if (Status == ServerRequestStatus.NoData)
            {
                return Result.Fail<FileTransfer>("Request from remote server has not been received.");
            }

            FileTransferId = fileTransferId;

            var inboundFileTransfer = new FileTransfer(_bufferSize)
            {
                Id = fileTransferId,
                TransferDirection = FileTransferDirection.Inbound,
                Initiator = FileTransferInitiator.RemoteServer,
                Status = FileTransferStatus.AwaitingResponse,
                TransferResponseCode = _fileTransferResponseCode,
                RemoteServerRetryLimit = _fileTransferRetryLimit,
                MyLocalIpAddress = localServerInfo.LocalIpAddress,
                MyPublicIpAddress = localServerInfo.PublicIpAddress,
                MyServerPortNumber = localServerInfo.PortNumber,
                RemoteServerIpAddress = _remoteServerIpAddress,
                RemoteServerPortNumber = _remoteServerPortNumber,
                LocalFilePath = _localFilePath,
                LocalFolderPath = _localFolderPath,
                FileSizeInBytes = _fileSizeBytes,
                RequestInitiatedTime = DateTime.Now
            };

            return RequestType == ServerRequestType.InboundFileTransferRequest
                ? Result.Ok(inboundFileTransfer)
                : Result.Fail<FileTransfer>($"Request received is not valid for this operation ({RequestType}).");
        }

        public Result<FileTransfer> GetOutboundFileTransfer(ServerInfo localServerInfo, int fileTransferId)
        {
            if (Status == ServerRequestStatus.NoData)
            {
                return Result.Fail<FileTransfer>("Request from remote server has not been received.");
            }

            FileTransferId = fileTransferId;

            var outboundFileTransfer = new FileTransfer(_bufferSize)
            {
                Id = fileTransferId,
                RemoteServerTransferId = _fileTransferId,
                TransferDirection = FileTransferDirection.Outbound,
                Initiator = FileTransferInitiator.RemoteServer,
                Status = FileTransferStatus.AwaitingResponse,
                TransferResponseCode = DateTime.Now.Ticks,
                MyLocalIpAddress = localServerInfo.LocalIpAddress,
                MyPublicIpAddress = localServerInfo.PublicIpAddress,
                MyServerPortNumber = localServerInfo.PortNumber,
                RemoteServerIpAddress = _remoteServerIpAddress,
                RemoteServerPortNumber = _remoteServerPortNumber,
                LocalFilePath = _requestedFilePath,
                LocalFolderPath = Path.GetDirectoryName(_requestedFilePath),
                RemoteFolderPath = _remoteFolderPath,
                RemoteFilePath = Path.Combine(_remoteFolderPath, Path.GetFileName(_requestedFilePath)),
                FileSizeInBytes = new FileInfo(_requestedFilePath).Length,
                RequestInitiatedTime = DateTime.Now
            };

            return RequestType == ServerRequestType.OutboundFileTransferRequest
                ? Result.Ok(outboundFileTransfer)
                : Result.Fail<FileTransfer>($"Request received is not valid for this operation ({RequestType}).");
        }

        public Result<long> GetFileTransferResponseCode()
        {
            if (Status == ServerRequestStatus.NoData)
            {
                return Result.Fail<long>("Request from remote server has not been received.");
            }

            return RequestTypeIsFileTransferResponse
                ? Result.Ok(_fileTransferResponseCode)
                : Result.Fail<long>($"Request received is not valid for this operation ({RequestType}).");
        }

        public Result<int> GetRemoteServerFileTransferId()
        {
            if (Status == ServerRequestStatus.NoData)
            {
                return Result.Fail<int>("Request from remote server has not been received.");
            }

            return RequestTypeIsFileTransferError
                ? Result.Ok(_fileTransferId)
                : Result.Fail<int>($"Request received is not valid for this operation ({RequestType}).");
        }

        public Result<int> GetInboundFileTransferId()
        {
            if (Status == ServerRequestStatus.NoData)
            {
                return Result.Fail<int>("Request from remote server has not been received.");
            }

            if (RequestType != ServerRequestType.InboundFileTransferRequest)
            {
                return Result.Fail<int>($"Request received is not valid for this operation ({RequestType}).");
            }

            const string error =
                "File Transfer was initiated by remote server, please call GetInboundFileTransfer() " +
                "to retrieve all transfer details.";

            return _fileTransferId != 0
                ? Result.Ok(_fileTransferId)
                : Result.Fail<int>(error);
        }

        public Result<FileTransfer> UpdateInboundFileTransfer(FileTransfer fileTransfer)
        {
            if (Status == ServerRequestStatus.NoData)
            {
                return Result.Fail<FileTransfer>("Request from remote server has not been received.");
            }

            return RequestType == ServerRequestType.InboundFileTransferRequest
                ? Result.Ok(UpdateTransferDetails(fileTransfer))
                : Result.Fail<FileTransfer>($"Request received is not valid for this operation ({RequestType}).");
        }

        FileTransfer UpdateTransferDetails(FileTransfer fileTransfer)
        {
            FileTransferId = fileTransfer.Id;

            fileTransfer.TransferResponseCode = _fileTransferResponseCode;
            fileTransfer.FileSizeInBytes = _fileSizeBytes;
            fileTransfer.RemoteServerRetryLimit = _fileTransferRetryLimit;

            if (_fileTransferRetryCounter == 1) return fileTransfer;

            fileTransfer.RetryCounter = _fileTransferRetryCounter;
            fileTransfer.Status = FileTransferStatus.AwaitingResponse;
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
            if (Status == ServerRequestStatus.NoData)
            {
                return Result.Fail<Socket>("Request from remote server has not been received.");
            }

            return RequestType == ServerRequestType.FileTransferAccepted
                ? Result.Ok(_socket)
                : Result.Fail<Socket>($"Request received is not valid for this operation ({RequestType}).");
        }

        public Result<FileTransfer> GetRetryLockoutDetails(FileTransfer fileTransfer)
        {
            if (Status == ServerRequestStatus.NoData)
            {
                return Result.Fail<FileTransfer>("Request from remote server has not been received.");
            }

            return RequestType == ServerRequestType.RetryLimitExceeded
                ? Result.Ok(ApplyRetryLockoutDetails(fileTransfer))
                : Result.Fail<FileTransfer>($"Request received is not valid for this operation ({RequestType}).");
        }

        FileTransfer ApplyRetryLockoutDetails(FileTransfer fileTransfer)
        {
            fileTransfer.RemoteServerRetryLimit = _fileTransferRetryLimit;
            fileTransfer.RetryLockoutExpireTime = new DateTime(_lockoutExpireTimeTicks);

            return fileTransfer;
        }

        public Result<string> GetLocalFolderPath()
        {
            if (Status == ServerRequestStatus.NoData)
            {
                return Result.Fail<string>("Request from remote server has not been received.");
            }

            return RequestType == ServerRequestType.FileListRequest
                ? Result.Ok(_localFolderPath)
                : Result.Fail<string>($"Request received is not valid for this operation ({RequestType}).");
        }

        public Result<FileInfoList> GetRemoteServerFileInfoList()
        {
            if (Status == ServerRequestStatus.NoData)
            {
                return Result.Fail<FileInfoList>("Request from remote server has not been received.");
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
            catch (SocketException ex)
            {
                var errorMessage = $"{ex.Message} ({ex.GetType()} raised in method ServerRequestController.ShutdownSocket)";
                return Result.Fail(errorMessage);
            }

            return Result.Ok();
        }
    }
}
