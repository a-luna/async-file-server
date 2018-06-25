using AaronLuna.AsyncFileServer.Utilities;

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
    using Common.Logging;
    using Common.Result;

    using Model;

    public class ServerRequestController
    {
        readonly Logger _log = new Logger(typeof(ServerRequestController));
        readonly ServerInfo _localServerInfo;
        readonly byte[] _buffer;
        readonly int _bufferSize;
        readonly int _timeoutMs;
        int _totalBytes;
        int _lastBytesReceivedCount;
        bool _requestReceived;

        readonly List<ServerEvent> _eventLog;
        IPAddress _remoteServerIpAddress;
        IPAddress _remoteServerLocalIpAddress;
        IPAddress _remoteServerPublicIpAddress;
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
        
        public ServerRequestController(ServerInfo localServerInfo, int bufferSize, int timeoutMs)
        {
            UnreadBytes = new List<byte>();

            _requestReceived = false;
            _localServerInfo = localServerInfo;
            _bufferSize = bufferSize;
            _timeoutMs = timeoutMs;
            _buffer = new byte[bufferSize];            
            _eventLog = new List<ServerEvent>();
        }

        public Socket Socket { get; set; }
        public List<byte> UnreadBytes { get; set; }
        public ServerRequest Request { get; private set; }
        public int RequestId => Request.Id;
        public ServerInfo RemoteServerInfo => Request.RemoteServerInfo;
        public bool ProcessRequestImmediately => Request.ProcessRequestImmediately;
        public bool InboundFileTransferRequested => Request.Type == ServerRequestType.InboundFileTransferRequest;
        
        public event EventHandler<ServerEvent> EventOccurred;
        public event EventHandler<ServerEvent> SocketEventOccurred;
        
        public async Task<Result<ServerRequest>> ReceiveServerRequestAsync(Socket socket)
        {
            Socket = socket;
            Request = new ServerRequest();

            _eventLog.Add(new ServerEvent {EventType = ServerEventType.ReceiveRequestFromRemoteServerStarted});
            EventOccurred?.Invoke(this, _eventLog.Last());

            var receiveRequestLength = await ReceiveLengthOfIncomingRequest().ConfigureAwait(false);
            if (receiveRequestLength.Failure)
            {
                return Result.Fail<ServerRequest>(receiveRequestLength.Error);
            }

            _totalBytes = receiveRequestLength.Value;

            var receiveRequestBytes = await ReceiveIncomingRequestBytes().ConfigureAwait(false);
            if (receiveRequestBytes.Failure)
            {
                return Result.Fail<ServerRequest>(receiveRequestBytes.Error);
            }
            
            var requestBytes = receiveRequestBytes.Value;
            ReadRequestBytes(requestBytes);
            
            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceiveRequestFromRemoteServerComplete,
                RemoteServerIpAddress = _remoteServerIpAddress,
                RequestType = Request.Type
            });

            EventOccurred?.Invoke(this, _eventLog.Last());
            Request.EventLog.AddRange(_eventLog);

            return Result.Ok(Request);
        }

        public Result<TextMessage> GetTextMessage()
        {
            if (!_requestReceived)
            {
                return Result.Fail<TextMessage>("Request from remote server has not been received.");
            }

            if (Request.Type != ServerRequestType.TextMessage)
            {
                return Result.Fail<TextMessage>($"Request received is not valid for this operation ({Request.Type}).");
            }

            var textMessage = new TextMessage
            {
                TimeStamp = Request.Timestamp,
                Author = TextMessageAuthor.RemoteServer,
                Message = _textMessage
            };

            return Result.Ok(textMessage);
        }

        public Result<FileTransfer> GetInboundFileTransfer()
        {
            if (!_requestReceived)
            {
                return Result.Fail<FileTransfer>("Request from remote server has not been received.");
            }

            if (Request.Type != ServerRequestType.InboundFileTransferRequest)
            {
                return Result.Fail<FileTransfer>($"Request received is not valid for this operation ({Request.Type}).");
            }

            var inboundFileTransfer = new FileTransfer(_bufferSize)
            {
                TransferDirection = FileTransferDirection.Inbound,
                Initiator = FileTransferInitiator.RemoteServer,
                Status = FileTransferStatus.AwaitingResponse,
                TransferResponseCode = _fileTransferResponseCode,
                RemoteServerRetryLimit = _fileTransferRetryLimit,
                MyLocalIpAddress = _localServerInfo.LocalIpAddress,
                MyPublicIpAddress = _localServerInfo.PublicIpAddress,
                MyServerPortNumber = _localServerInfo.PortNumber,
                RemoteServerIpAddress = _remoteServerIpAddress,
                RemoteServerPortNumber = _remoteServerPortNumber,
                LocalFilePath = _localFilePath,
                LocalFolderPath = _localFolderPath,
                FileSizeInBytes = _fileSizeBytes,
                RequestInitiatedTime = DateTime.Now,
                ReceiveSocket = Socket
            };

            return Result.Ok(inboundFileTransfer);
        }

        public Result<FileTransfer> GetOutboundFileTransfer()
        {
            if (!_requestReceived)
            {
                return Result.Fail<FileTransfer>("Request from remote server has not been received.");
            }

            if (Request.Type != ServerRequestType.OutboundFileTransferRequest)
            {
                return Result.Fail<FileTransfer>($"Request received is not valid for this operation ({Request.Type}).");
            }

            var outboundFileTransfer = new FileTransfer(_bufferSize)
            {
                RemoteServerTransferId = _fileTransferId,
                TransferDirection = FileTransferDirection.Outbound,
                Initiator = FileTransferInitiator.RemoteServer,
                Status = FileTransferStatus.AwaitingResponse,
                TransferResponseCode = DateTime.Now.Ticks,
                MyLocalIpAddress = _localServerInfo.LocalIpAddress,
                MyPublicIpAddress = _localServerInfo.PublicIpAddress,
                MyServerPortNumber = _localServerInfo.PortNumber,
                RemoteServerIpAddress = _remoteServerIpAddress,
                RemoteServerPortNumber = _remoteServerPortNumber,
                LocalFilePath = _requestedFilePath,
                LocalFolderPath = Path.GetDirectoryName(_requestedFilePath),
                RemoteFolderPath = _remoteFolderPath,
                RemoteFilePath = Path.Combine(_remoteFolderPath, Path.GetFileName(_requestedFilePath)),
                FileSizeInBytes = new FileInfo(_requestedFilePath).Length,
                RequestInitiatedTime = DateTime.Now
            };

            return Result.Ok(outboundFileTransfer);
        }

        public Result<long> GetFileTransferResponseCode()
        {
            if (!_requestReceived)
            {
                return Result.Fail<long>("Request from remote server has not been received.");
            }

            if (!Request.IsFileTransferResponse)
            {
                return Result.Fail<long>($"Request received is not valid for this operation ({Request.Type}).");
            }

            return Result.Ok(_fileTransferResponseCode);
        }

        public Result<int> GetRemoteServerFileTransferId()
        {
            if (!_requestReceived)
            {
                return Result.Fail<int>("Request from remote server has not been received.");
            }

            if (!Request.IsFIleTransferError)
            {
                return Result.Fail<int>($"Request received is not valid for this operation ({Request.Type}).");
            }

            return Result.Ok(Convert.ToInt32(_fileTransferId));
        }

        public Result<int> GetInboundFileTransferId()
        {
            if (!_requestReceived)
            {
                return Result.Fail<int>("Request from remote server has not been received.");
            }

            if (Request.Type != ServerRequestType.InboundFileTransferRequest)
            {
                return Result.Fail<int>($"Request received is not valid for this operation ({Request.Type}).");
            }

            const string error =
                "File Transfer was initiated by remote server, please call GetInboundFileTransfer() " +
                "to retrieve all transfer details.";

            return _fileTransferId != 0
                   ? Result.Ok(_fileTransferId)
                    : Result.Fail<int>(error);
        }

        public Result<FileTransfer> SynchronizeFileTransferDetails(FileTransfer fileTransfer)
        {
            if (!_requestReceived)
            {
                return Result.Fail<FileTransfer>("Request from remote server has not been received.");
            }

            if (Request.Type != ServerRequestType.InboundFileTransferRequest)
            {
                return Result.Fail<FileTransfer>($"Request received is not valid for this operation ({Request.Type}).");
            }

            fileTransfer.ReceiveSocket = Socket;
            fileTransfer.TransferResponseCode = _fileTransferResponseCode;
            fileTransfer.FileSizeInBytes = _fileSizeBytes;
            fileTransfer.RemoteServerRetryLimit = _fileTransferRetryLimit;

            if (_fileTransferRetryCounter == 1) return Result.Ok(fileTransfer);

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

            return Result.Ok(fileTransfer);
        }

        public Result<FileTransfer> GetRetryLockoutDetails(FileTransfer fileTransfer)
        {
            if (!_requestReceived)
            {
                return Result.Fail<FileTransfer>("Request from remote server has not been received.");
            }

            if (Request.Type != ServerRequestType.RetryLimitExceeded)
            {
                return Result.Fail<FileTransfer>($"Request received is not valid for this operation ({Request.Type}).");
            }

            fileTransfer.Status = FileTransferStatus.RetryLimitExceeded;
            fileTransfer.RemoteServerRetryLimit = _fileTransferRetryLimit;
            fileTransfer.RetryLockoutExpireTime = new DateTime(_lockoutExpireTimeTicks);

            return Result.Ok(fileTransfer);
        }

        public Result<string> GetLocalFolderPath()
        {
            if (!_requestReceived)
            {
                return Result.Fail<string>("Request from remote server has not been received.");
            }

            if (Request.Type != ServerRequestType.FileListRequest)
            {
                return Result.Fail<string>($"Request received is not valid for this operation ({Request.Type}).");
            }

            return Result.Ok(_localFolderPath);
        }

        public Result<FileInfoList> GetRemoteServerFileInfoList()
        {
            if (!_requestReceived)
            {
                return Result.Fail<FileInfoList>("Request from remote server has not been received.");
            }

            if (Request.Type != ServerRequestType.FileListResponse)
            {
                return Result.Fail<FileInfoList>($"Request received is not valid for this operation ({Request.Type}).");
            }

            return Result.Ok(_fileInfoList);
        }

        public Result ShutdownSocket()
        {
            try
            {
                Socket.Shutdown(SocketShutdown.Send);
                Socket.Close();
            }
            catch (SocketException ex)
            {
                var errorMessage = $"{ex.Message} ({ex.GetType()} raised in method ServerRequestController.ShutdownSocket)";
                return Result.Fail(errorMessage);
            }

            return Result.Ok();
        }

        async Task<Result<int>> ReceiveLengthOfIncomingRequest()
        {
            _eventLog.Add(new ServerEvent {EventType = ServerEventType.ReceiveRequestLengthStarted});
            EventOccurred?.Invoke(this, _eventLog.Last());

            var readFromSocketResult =
                await Socket.ReceiveWithTimeoutAsync(
                        _buffer,
                        0,
                        _bufferSize,
                        SocketFlags.None,
                        _timeoutMs)
                    .ConfigureAwait(false);
            
            if (readFromSocketResult.Failure)
            {
                return readFromSocketResult;
            }

            _lastBytesReceivedCount = readFromSocketResult.Value;
            var unreadBytesCount = _lastBytesReceivedCount - Constants.SizeOfInt32InBytes;
            var requestLength = BitConverter.ToInt32(_buffer, 0);

            var requestLengthBytes = new byte[Constants.SizeOfInt32InBytes];
            _buffer.ToList().CopyTo(0, requestLengthBytes, 0, Constants.SizeOfInt32InBytes);

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.PreserveExtraBytesReceivedWithIncomingRequestLength,
                BytesReceivedCount = _lastBytesReceivedCount,
                RequestLengthInBytes = Constants.SizeOfInt32InBytes,
                UnreadBytesCount = unreadBytesCount
            });

            SocketEventOccurred?.Invoke(this, _eventLog.Last());

            if (_lastBytesReceivedCount > Constants.SizeOfInt32InBytes)
            {
                var unreadBytes = new byte[unreadBytesCount];
                _buffer.ToList().CopyTo(Constants.SizeOfInt32InBytes, unreadBytes, 0, unreadBytesCount);
                UnreadBytes = unreadBytes.ToList();

                _eventLog.Add(new ServerEvent
                {
                    EventType = ServerEventType.SaveUnreadBytesAfterRequestLengthReceived,
                    CurrentRequestBytesReceived = _lastBytesReceivedCount,
                    ExpectedByteCount = Constants.SizeOfInt32InBytes,
                    UnreadBytesCount = unreadBytesCount,
                });

                EventOccurred?.Invoke(this, _eventLog.Last());
            }

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceiveRequestLengthComplete,
                RequestLengthInBytes = requestLength,
                RequestLengthData = requestLengthBytes
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            return Result.Ok(requestLength);
        }

        async Task<Result<byte[]>> ReceiveIncomingRequestBytes()
        {
            _eventLog.Add(new ServerEvent {EventType = ServerEventType.ReceiveRequestBytesStarted});
            EventOccurred?.Invoke(this, _eventLog.Last());

            int currentRequestBytesReceived;
            var socketReadCount = 0;
            var bytesReceived = 0;
            var bytesRemaining = _totalBytes;
            var requestBytes = new List<byte>();
            var unreadByteCount = 0;

            if (UnreadBytes.Count > 0)
            {
                currentRequestBytesReceived = Math.Min(_totalBytes, UnreadBytes.Count);
                var unreadRequestBytes = new byte[currentRequestBytesReceived];

                UnreadBytes.CopyTo(0, unreadRequestBytes, 0, currentRequestBytesReceived);
                requestBytes.AddRange(unreadRequestBytes.ToList());

                bytesReceived += currentRequestBytesReceived;
                bytesRemaining -= currentRequestBytesReceived;

                _eventLog.Add(new ServerEvent
                {
                    EventType = ServerEventType.CopySavedBytesToRequestData,
                    UnreadBytesCount = UnreadBytes.Count,
                    TotalRequestBytesReceived = currentRequestBytesReceived,
                    RequestLengthInBytes = _totalBytes,
                    RequestBytesRemaining = bytesRemaining
                });

                EventOccurred?.Invoke(this, _eventLog.Last());

                if (UnreadBytes.Count > _totalBytes)
                {
                    var fileByteCount = UnreadBytes.Count - _totalBytes;
                    var fileBytes = new byte[fileByteCount];
                    UnreadBytes.CopyTo(_totalBytes, fileBytes, 0, fileByteCount);
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
                var readFromSocketResult =
                    await Socket.ReceiveWithTimeoutAsync(
                            _buffer,
                            0,
                            _bufferSize,
                            SocketFlags.None,
                            _timeoutMs)
                        .ConfigureAwait(false);

                if (readFromSocketResult.Failure)
                {
                    return Result.Fail<byte[]>(readFromSocketResult.Error);
                }

                _lastBytesReceivedCount = readFromSocketResult.Value;
                currentRequestBytesReceived = Math.Min(bytesRemaining, _lastBytesReceivedCount);

                var requestBytesFromSocket = new byte[currentRequestBytesReceived];
                _buffer.ToList().CopyTo(0, requestBytesFromSocket, 0, currentRequestBytesReceived);
                requestBytes.AddRange(requestBytesFromSocket.ToList());

                socketReadCount++;
                unreadByteCount = _lastBytesReceivedCount - currentRequestBytesReceived;
                bytesReceived += currentRequestBytesReceived;
                bytesRemaining -= currentRequestBytesReceived;

                _eventLog.Add(new ServerEvent
                {
                    EventType = ServerEventType.ReceivedRequestBytesFromSocket,
                    SocketReadCount = socketReadCount,
                    BytesReceivedCount = _lastBytesReceivedCount,
                    CurrentRequestBytesReceived = currentRequestBytesReceived,
                    TotalRequestBytesReceived = bytesReceived,
                    RequestLengthInBytes = _totalBytes,
                    RequestBytesRemaining = bytesRemaining,
                    UnreadBytesCount = unreadByteCount
                });

                SocketEventOccurred?.Invoke(this, _eventLog.Last());
            }

            if (unreadByteCount > 0)
            {
                var unreadBytes = new byte[unreadByteCount];
                _buffer.ToList().CopyTo(currentRequestBytesReceived, unreadBytes, 0, unreadByteCount);
                UnreadBytes = unreadBytes.ToList();

                _eventLog.Add(new ServerEvent
                {
                    EventType = ServerEventType.PreserveExtraBytesReceivedAfterAllRequestBytesWereReceived,
                    ExpectedByteCount = currentRequestBytesReceived,
                    UnreadBytesCount = unreadByteCount
                });

                EventOccurred?.Invoke(this, _eventLog.Last());
            }

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceiveRequestBytesComplete,
                MessageData = requestBytes.ToArray()
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            return Result.Ok(requestBytes.ToArray());
        }

        void ReadRequestBytes(byte[] requestData)
        {
            var requestTypeData = BitConverter.ToInt32(requestData, 0).ToString();
            Request.Type = (ServerRequestType)Enum.Parse(typeof(ServerRequestType), requestTypeData);

            (_remoteServerIpAddress,
                _remoteServerPortNumber,
                _textMessage,
                _fileTransferId,
                _remoteServerLocalIpAddress,
                _remoteServerPublicIpAddress,
                _requestedFilePath,
                _localFilePath,
                _localFolderPath,
                _remoteFolderPath,
                _fileInfoList,
                _fileSizeBytes,
                _fileTransferResponseCode,
                _lockoutExpireTimeTicks,
                _fileTransferRetryCounter,
                _fileTransferRetryLimit) = ServerRequestDataReader.ReadDataForRequestType(Request.Type, requestData);

            if (Request.Type == ServerRequestType.ServerInfoResponse)
            {
                Request.RemoteServerInfo =
                    new ServerInfo
                    {
                        LocalIpAddress = _remoteServerLocalIpAddress,
                        PublicIpAddress = _remoteServerPublicIpAddress,
                        PortNumber = _remoteServerPortNumber,
                        TransferFolder = _remoteFolderPath
                    };
            }
            else
            {
                Request.RemoteServerInfo =
                    new ServerInfo
                    {
                        SessionIpAddress = _remoteServerIpAddress,
                        PortNumber = _remoteServerPortNumber,
                        TransferFolder = _remoteFolderPath
                    };
            }

            _requestReceived = true;
        }
    }
}
