namespace TplSockets
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;

    using AaronLuna.Common.IO;
    using AaronLuna.Common.Logging;
    using AaronLuna.Common.Network;
    using AaronLuna.Common.Result;

    public class TplSocketServer
    {   
        const string NotInitializedMessage =
            "Server is unitialized and cannot handle incoming connections";

        int _initialized;
        int _idle;
        int _remoteServerAcceptedFileTransfer;
        int _remoteServerRejectedFileTransfer;
        int _transferInProgress;
        int _inboundFileTransferIsStalled;
        int _outboundFileTransferIsStalled;
        int _shutdownInitiated;
        int _shutdownComplete;
        int _requestId;
        int _fileTransferId;

        readonly Logger _log = new Logger(typeof(TplSocketServer));
        readonly List<ServerEvent> _eventLog;
        readonly List<ServerRequest> _requestQueue;
        readonly List<ServerRequest> _requestArchive;
        readonly List<FileTransfer> _fileTransfers;
        readonly ServerState _state;
        readonly Socket _listenSocket;
        //Socket _transferSocket;
        //Socket _clientSocket;
        IPAddress _ipAddressLastConnection;
        CancellationToken _token;
        static readonly object FileLock = new object();

        public bool Initialized
        {
            get => (Interlocked.CompareExchange(ref _initialized, 1, 1) == 1);
            set
            {
                if (value) Interlocked.CompareExchange(ref _initialized, 1, 0);
                else Interlocked.CompareExchange(ref _initialized, 0, 1);
            }
        }

        public bool IsListening
        {
            get => (Interlocked.CompareExchange(ref _shutdownComplete, 1, 1) == 1);
            set
            {
                if (value) Interlocked.CompareExchange(ref _shutdownComplete, 1, 0);
                else Interlocked.CompareExchange(ref _shutdownComplete, 0, 1);
            }
        }

        public bool IsIdle
        {
            get => Interlocked.CompareExchange(ref _idle, 1, 1) == 1;
            set
            {
                if (value) Interlocked.CompareExchange(ref _idle, 1, 0);
                else Interlocked.CompareExchange(ref _idle, 0, 1);
            }
        }

        bool RemoteServerAcceptedFileTransfer
        {
            get => Interlocked.CompareExchange(ref _remoteServerAcceptedFileTransfer, 1, 1) == 1;
            set
            {
                if (value) Interlocked.CompareExchange(ref _remoteServerAcceptedFileTransfer, 1, 0);
                else Interlocked.CompareExchange(ref _remoteServerAcceptedFileTransfer, 0, 1);
            }
        }

        bool RemoteServerRejectedFileTransfer
        {
            get => Interlocked.CompareExchange(ref _remoteServerRejectedFileTransfer, 1, 1) == 1;
            set
            {
                if (value) Interlocked.CompareExchange(ref _remoteServerRejectedFileTransfer, 1, 0);
                else Interlocked.CompareExchange(ref _remoteServerRejectedFileTransfer, 0, 1);
            }
        }

        bool TransferInProgress
        {
            get => Interlocked.CompareExchange(ref _transferInProgress, 1, 1) == 1;
            set
            {
                if (value) Interlocked.CompareExchange(ref _transferInProgress, 1, 0);
                else Interlocked.CompareExchange(ref _transferInProgress, 0, 1);
            }
        }

        bool InboundFileTransferStalled
        {
            get => Interlocked.CompareExchange(ref _inboundFileTransferIsStalled, 1, 1) == 1;
            set
            {
                if (value) Interlocked.CompareExchange(ref _inboundFileTransferIsStalled, 1, 0);
                else Interlocked.CompareExchange(ref _inboundFileTransferIsStalled, 0, 1);
            }
        }

        bool OutboundFileTransferStalled
        {
            get => Interlocked.CompareExchange(ref _outboundFileTransferIsStalled, 1, 1) == 1;
            set
            {
                if (value) Interlocked.CompareExchange(ref _outboundFileTransferIsStalled, 1, 0);
                else Interlocked.CompareExchange(ref _outboundFileTransferIsStalled, 0, 1);
            }
        }
        
        bool ShutdownInitiated
        {
            get => Interlocked.CompareExchange(ref _shutdownInitiated, 1, 1) == 1;
            set
            {
                if (value) Interlocked.CompareExchange(ref _shutdownInitiated, 1, 0);
                else Interlocked.CompareExchange(ref _shutdownInitiated, 0, 1);
            }
        }

        public TplSocketServer()
        {
            Initialized = false;
            IsListening = false;
            IsIdle = true;
            InboundFileTransferStalled = false;
            OutboundFileTransferStalled = false;
            ShutdownInitiated = false;
            
            SocketSettings = new SocketSettings
            {
                ListenBacklogSize = 5,
                BufferSize = 1024,
                SocketTimeoutInMilliseconds = 5000
            };

            Info = new ServerInfo()
            {
                TransferFolder = GetDefaultTransferFolder()
            };

            RemoteServerInfo = new ServerInfo();
            RemoteServerFileList = new FileInfoList();
            
            _state = new ServerState();
            _requestId = 1;
            _fileTransferId = 1;
            _listenSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            //_transferSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            //_clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _eventLog = new List<ServerEvent>();
            _requestQueue = new List<ServerRequest>();
            _requestArchive = new List<ServerRequest>();
            _fileTransfers = new List<FileTransfer>();
        }
        
        public bool QueueIsEmpty => _requestQueue.Count == 0;
        public int RequestsInQueue => _requestQueue.Count;
        public ServerRequest OldestRequestInQueue => _requestQueue.First();

        public bool NoFileTransfers => _fileTransfers.Count == 0;
        public List<int> FileTransferIds => _fileTransfers.Select(t => t.Id).ToList();
        public int OldestTransferId => _fileTransfers.First().Id;
        public int NewestTransferId => _fileTransfers.Last().Id;
        
        public List<int> StalledTransfersIds =>
            _fileTransfers.Select(t => t)
                .Where(t => t.Status == FileTransferStatus.Stalled)
                .Select(t => t.Id).ToList();
        
        public float TransferUpdateInterval { get; set; }
        public int TransferRetryLimit { get; set; }
        public TimeSpan RetryLimitLockout { get; set; }

        public SocketSettings SocketSettings { get; set; }
        public int ListenBacklogSize => SocketSettings.ListenBacklogSize;
        public int BufferSize => SocketSettings.BufferSize;
        public int SocketTimeoutInMilliseconds => SocketSettings.SocketTimeoutInMilliseconds;

        public PlatformID OperatingSystem => Environment.OSVersion.Platform;
        public ServerInfo Info { get; set; }

        public string MyTransferFolderPath
        {
            get => Info.TransferFolder;
            set => Info.TransferFolder = value;
        }

        public IPAddress MyLocalIpAddress => Info.LocalIpAddress;
        public IPAddress MyPublicIpAddress => Info.PublicIpAddress;
        public int MyServerPortNumber => Info.PortNumber;

        public ServerInfo RemoteServerInfo { get; set; }

        public string RemoteServerTransferFolderPath
        {
            get => RemoteServerInfo.TransferFolder;
            set => RemoteServerInfo.TransferFolder = value;
        }

        public IPAddress RemoteServerSessionIpAddress => RemoteServerInfo.SessionIpAddress;
        public IPAddress RemoteServerLocalIpAddress => RemoteServerInfo.LocalIpAddress;
        public IPAddress RemoteServerPublicIpAddress => RemoteServerInfo.PublicIpAddress;
        public int RemoteServerPortNumber => RemoteServerInfo.PortNumber;

        public FileInfoList RemoteServerFileList { get; set; }

        public bool FileTransferAccepted => RemoteServerAcceptedFileTransfer;
        public bool FileTransferRejected => RemoteServerRejectedFileTransfer;
        public bool FileTransferInProgress => TransferInProgress;
        public bool FileTransferCanceled => OutboundFileTransferStalled;
        public bool FileTransferStalled => InboundFileTransferStalled;

        public string RemoteFilePath
        {
            get => _state.RemoteFilePath;
            set => _state.RemoteFilePath = value;
        }

        public string OutgoingFilePath
        {
            get => _state.OutgoingFilePath;
            set => _state.OutgoingFilePath = value;
        }

        public string IncomingFilePath
        {
            get => _state.IncomingFilePath;
            set => _state.IncomingFilePath = value;
        }

        public event EventHandler<ServerEvent> EventOccurred;
        public event EventHandler<ServerEvent> SocketEventOccurred;
        public event EventHandler<ServerEvent> FileTransferProgress;

        static string GetDefaultTransferFolder()
        {
            var defaultPath = $"{Directory.GetCurrentDirectory()}{Path.DirectorySeparatorChar}transfer";

            if (!Directory.Exists(defaultPath))
            {
                Directory.CreateDirectory(defaultPath);
            }

            return defaultPath;
        }

        public Result<FileTransfer> GetFileTransferById(int id)
        {
            var matches = _fileTransfers.Select(t => t).Where(t => t.Id == id).ToList();

            if (matches.Count == 0)
            {
                return Result.Fail<FileTransfer>($"No file transfer was found with an ID value of {id}");
            }

            if (matches.Count > 1)
            {
                return Result.Fail<FileTransfer>($"Found {matches.Count} file transfers with the same ID value of {id}");
            }

            var requestedFileTransfer = matches[0];

            requestedFileTransfer.EventLog =
                _eventLog.Select(e => e).Where(e => e.FileTransferId == requestedFileTransfer.Id).ToList();

            return Result.Ok(requestedFileTransfer);
        }

        Result<FileTransfer> GetFileTransferByResponseCode(long responseCode)
        {
            var matches = _fileTransfers.Select(t => t).Where(t => t.TransferResponseCode == responseCode).ToList();

            if (matches.Count == 0)
            {
                return Result.Fail<FileTransfer>($"No file transfer was found with a response code value of {responseCode}");
            }

            if (matches.Count > 1)
            {
                return Result.Fail<FileTransfer>($"Found {matches.Count} file transfers with the same response code value of {responseCode}");
            }

            var requestedFileTransfer = matches[0];

            requestedFileTransfer.EventLog =
                _eventLog.Select(e => e).Where(e => e.FileTransferId == requestedFileTransfer.Id).ToList();

            return Result.Ok(requestedFileTransfer);
        }

        public async Task InitializeAsync(string cidrIp, int port)
        {
            if (Initialized) return;
            
            var getLocalIpResult = NetworkUtilities.GetLocalIPv4Address(cidrIp);

            var localIp = getLocalIpResult.Success
                ? getLocalIpResult.Value
                : IPAddress.Loopback;
            
            var getPublicIResult =
                await NetworkUtilities.GetPublicIPv4AddressAsync().ConfigureAwait(false);

            var publicIp = getPublicIResult.Success
                ? getPublicIResult.Value
                : IPAddress.None;

            Info = new ServerInfo
            {
                PortNumber = port,
                LocalIpAddress = localIp,
                PublicIpAddress = publicIp
            };

            if (getLocalIpResult.Success)
            {
                Info.SessionIpAddress = localIp;
            }
            else if (getPublicIResult.Success)
            {
                Info.SessionIpAddress = publicIp;
            }

            Initialized = true;
        }

        public async Task<Result> RunAsync(CancellationToken token)
        {
            if (!Initialized)
            {
                return Result.Fail(NotInitializedMessage);
            }

            _token = token;
            Logger.Start("server.log");

            var listenResult = Listen(MyServerPortNumber);
            if (listenResult.Failure)
            {
                return listenResult;
            }

            IsListening = true;
            var runServerResult = await HandleIncomingConnectionsAsync().ConfigureAwait(false);

            IsListening = false;
            ShutdownListenSocket();

            EventOccurred?.Invoke(this,
             new ServerEvent { EventType = EventType.ServerStoppedListening });

            return runServerResult;
        }

        Result Listen(int localPort)
        {
            var ipEndPoint = new IPEndPoint(IPAddress.Any, localPort);
            try
            {
                _listenSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _listenSocket.Bind(ipEndPoint);
                _listenSocket.Listen(ListenBacklogSize);
            }
            catch (SocketException ex)
            {
                _log.Error("Error raised in method Listen", ex);
                return Result.Fail($"{ex.Message} ({ex.GetType()} raised in method TplSocketServer.Listen)");
            }

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ServerStartedListening,
                    LocalPortNumber = MyServerPortNumber
                });

            return Result.Ok();
        }

        async Task<Result> HandleIncomingConnectionsAsync()
        {
            // Main loop. Server handles incoming connections until shutdown command is received
            // or an error is encountered
            while (true)
            {
                var acceptResult = await _listenSocket.AcceptTaskAsync(_token).ConfigureAwait(false);
                if (acceptResult.Failure)
                {
                    return acceptResult;
                }

                var clientSocket = acceptResult.Value;
                if (!(clientSocket.RemoteEndPoint is IPEndPoint clientEndPoint))
                {
                    return Result.Fail("Error occurred casting _state._clientSocket.RemoteEndPoint as IPEndPoint");
                }

                _ipAddressLastConnection = clientEndPoint.Address;
                EventOccurred?.Invoke(this,
                    new ServerEvent
                    {
                        EventType = EventType.ConnectionAccepted,
                        RemoteServerIpAddress = _ipAddressLastConnection
                    });

                var receivedMessage = await ReceiveRequestFromClientAsync(clientSocket).ConfigureAwait(false);
                if (_token.IsCancellationRequested || ShutdownInitiated)
                {
                    return Result.Ok();
                }

                if (receivedMessage.Failure)
                {
                    return receivedMessage;
                }
            }
        }

        async Task<Result> ReceiveRequestFromClientAsync(Socket socket)
        {
            _state.Buffer = new byte[BufferSize];
            _state.UnreadBytes = new List<byte>();

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceiveMessageFromClientStarted,
                    RemoteServerIpAddress = _ipAddressLastConnection
                });

            var messageLengthResult = await DetermineMessageLengthAsync(socket).ConfigureAwait(false);
            if (messageLengthResult.Failure)
            {
                return Result.Fail<ServerRequest>(messageLengthResult.Error);
            }

            var messageLength = messageLengthResult.Value;

            var receiveMessageResult = await ReceiveAllMessageBytesAsync(socket, messageLength).ConfigureAwait(false);
            if (receiveMessageResult.Failure)
            {
                return Result.Fail<ServerRequest>(receiveMessageResult.Error);
            }

            var requestData = receiveMessageResult.Value;
            var requestTypeData = RequestUnwrapper.ReadInt32(requestData).ToString();
            var requestType = (RequestType)Enum.Parse(typeof(RequestType), requestTypeData);
            var request = new ServerRequest
            {
                Data = requestData,
                Type = requestType,
                RemoteServerIp = _ipAddressLastConnection,
                Socket = socket,
                Id = _requestId,
                Timestamp = DateTime.Now
            };

            _requestQueue.Add(request);
            _requestId++;

            var messageReceivedEvent = new ServerEvent
            {
                EventType = EventType.ReceiveMessageFromClientComplete,
                RemoteServerIpAddress = _ipAddressLastConnection,
                RequestType = request.Type
            };

            EventOccurred?.Invoke(this, messageReceivedEvent);
            if (!request.ProcessRequestImmediately()) return Result.Ok();
            if (!IsIdle) return Result.Ok();
            
            _requestQueue.Remove(request);
            var result = await ProcessRequestAsync(request);

            request.EventLog = _eventLog.Select(e => e).Where(e => e.RequestId == request.Id).ToList();
            _requestArchive.Add(request);
            
            return result;
        }

        async Task<Result<int>> ReadFromSocketAsync(Socket socket)
        {
            Result<int> receiveResult;
            try
            {
                receiveResult =
                    await socket.ReceiveWithTimeoutAsync(
                        _state.Buffer,
                        0,
                        BufferSize,
                        SocketFlags.None,
                        SocketTimeoutInMilliseconds)
                        .ConfigureAwait(false);
            }
            catch (SocketException ex)
            {
                _log.Error("Error raised in method DetermineTransferTypeAsync", ex);
                return Result.Fail<int>(
                    $"{ex.Message} ({ex.GetType()} raised in method TplSocketServer.DetermineTransferTypeAsync)");
            }
            catch (TimeoutException ex)
            {
                _log.Error("Error raised in method DetermineTransferTypeAsync", ex);
                return Result.Fail<int>(
                    $"{ex.Message} ({ex.GetType()} raised in method TplSocketServer.DetermineTransferTypeAsync)");
            }

            return receiveResult;
        }

        async Task<Result<int>> DetermineMessageLengthAsync(Socket socket)
        {
            EventOccurred?.Invoke(this,
                new ServerEvent
                    {EventType = EventType.DetermineMessageLengthStarted});

            var readFromSocketResult = await ReadFromSocketAsync(socket).ConfigureAwait(false);
            if (readFromSocketResult.Failure)
            {
                return readFromSocketResult;
            }

            _state.LastBytesReceivedCount = readFromSocketResult.Value;
            var numberOfUnreadBytes = _state.LastBytesReceivedCount - 4;
            var messageLength = RequestUnwrapper.ReadInt32(_state.Buffer);
            var messageLengthData = BitConverter.GetBytes(messageLength);

            SocketEventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceivedMessageLengthFromSocket,
                    BytesReceived = _state.LastBytesReceivedCount,
                    MessageLengthInBytes = 4,
                    UnreadByteCount = numberOfUnreadBytes
                });

            if (_state.LastBytesReceivedCount > 4)
            {
                var unreadBytes = new byte[numberOfUnreadBytes];
                _state.Buffer.ToList().CopyTo(4, unreadBytes, 0, numberOfUnreadBytes);
                _state.UnreadBytes = unreadBytes.ToList();

                EventOccurred?.Invoke(this,
                    new ServerEvent
                    {
                        EventType = EventType.SaveUnreadBytesAfterReceiveMessageLength,
                        CurrentMessageBytesReceived = _state.LastBytesReceivedCount,
                        ExpectedByteCount = 4,
                        UnreadByteCount = numberOfUnreadBytes,
                    });
            }

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.DetermineMessageLengthComplete,
                    MessageLengthInBytes = messageLength,
                    MessageLengthData = messageLengthData
                });

            return Result.Ok(messageLength);
        }

        async Task<Result<byte[]>> ReceiveAllMessageBytesAsync(Socket socket, int messageLength)
        {
            EventOccurred?.Invoke(this,
                new ServerEvent {EventType = EventType.ReceiveMessageBytesStarted});

            int messageByteCount;
            var socketReadCount = 0;
            var totalBytesReceived = 0;
            var bytesRemaining = messageLength;
            var messageData = new List<byte>();
            var newUnreadByteCount = 0;

            if (_state.UnreadBytes.Count > 0)
            {
                messageByteCount = Math.Min(messageLength, _state.UnreadBytes.Count);
                var messageBytes = new byte[messageByteCount];

                _state.UnreadBytes.CopyTo(0, messageBytes, 0, messageByteCount);
                messageData.AddRange(messageBytes.ToList());

                totalBytesReceived += messageByteCount;
                bytesRemaining -= messageByteCount;

                EventOccurred?.Invoke(this,
                    new ServerEvent
                    {
                        EventType = EventType.CopySavedBytesToMessageData,
                        UnreadByteCount = _state.UnreadBytes.Count,
                        TotalMessageBytesReceived = messageByteCount,
                        MessageLengthInBytes = messageLength,
                        MessageBytesRemaining = bytesRemaining
                    });

                if (_state.UnreadBytes.Count > messageLength)
                {
                    var fileByteCount = _state.UnreadBytes.Count - messageLength;
                    var fileBytes = new byte[fileByteCount];
                    _state.UnreadBytes.CopyTo(messageLength, fileBytes, 0, fileByteCount);
                    _state.UnreadBytes = fileBytes.ToList();
                }
                else
                {
                    _state.UnreadBytes = new List<byte>();
                }
            }

            messageByteCount = 0;
            while (bytesRemaining > 0)
            {
                var readFromSocketResult = await ReadFromSocketAsync(socket).ConfigureAwait(false);
                if (readFromSocketResult.Failure)
                {
                    return Result.Fail<byte[]>(readFromSocketResult.Error);
                }

                _state.LastBytesReceivedCount = readFromSocketResult.Value;

                messageByteCount = Math.Min(bytesRemaining, _state.LastBytesReceivedCount);
                var receivedBytes = new byte[messageByteCount];

                _state.Buffer.ToList().CopyTo(0, receivedBytes, 0, messageByteCount);
                messageData.AddRange(receivedBytes.ToList());

                socketReadCount++;
                newUnreadByteCount = _state.LastBytesReceivedCount - messageByteCount;
                totalBytesReceived += messageByteCount;
                bytesRemaining -= messageByteCount;

                SocketEventOccurred?.Invoke(this,
                    new ServerEvent
                    {
                        EventType = EventType.ReceivedMessageBytesFromSocket,
                        SocketReadCount = socketReadCount,
                        BytesReceived = _state.LastBytesReceivedCount,
                        CurrentMessageBytesReceived = messageByteCount,
                        TotalMessageBytesReceived = totalBytesReceived,
                        MessageLengthInBytes = messageLength,
                        MessageBytesRemaining = bytesRemaining,
                        UnreadByteCount = newUnreadByteCount
                    });
            }

            if (newUnreadByteCount > 0)
            {
                var unreadBytes = new byte[newUnreadByteCount];
                _state.Buffer.ToList().CopyTo(messageByteCount, unreadBytes, 0, newUnreadByteCount);
                _state.UnreadBytes = unreadBytes.ToList();

                EventOccurred?.Invoke(this,
                    new ServerEvent
                    {
                        EventType = EventType.SaveUnreadBytesAfterReceiveMessage,
                        ExpectedByteCount = messageByteCount,
                        UnreadByteCount = newUnreadByteCount
                    });
            }

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceiveMessageBytesComplete,
                    MessageData = messageData.ToArray()
                });

            return Result.Ok(messageData.ToArray());
        }

        public async Task<Result> ProcessNextMessageInQueueAsync()
        {
            return await ProcessRequestAsync(_requestQueue.First().Id);
        }

        public async Task<Result> ProcessRequestAsync(int messageId)
        {
            if (_requestQueue.Count <= 0)
            {
                return Result.Fail("Queue is empty");
            }

            if (!IsIdle)
            {
                return Result.Fail("Server is busy, please try again after the current operation has completed");
            }

            var checkQueue = _requestQueue.Select(m => m).Where(m => m.Id == messageId).ToList();
            var checkArchive = _requestArchive.Select(m => m).Where(m => m.Id == messageId).ToList();

            if (checkQueue.Count == 0 && checkArchive.Count == 1)
            {
                return Result.Fail($"ServerRequest ID# {messageId} has already been processed, event logs for this request are available in the archive.");
            }

            if (checkQueue.Count == 0 && checkArchive.Count == 0)
            {
                return Result.Fail($"ServerRequest ID# {messageId} appears to be invalid. No record of this request were found in the queue or the archive.");
            }

            if (checkQueue.Count == 1)
            {
                var message = checkQueue[0];
                _requestQueue.Remove(message);
                var result = await ProcessRequestAsync(message);

                message.EventLog = _eventLog.Select(e => e).Where(e => e.RequestId == message.Id).ToList();
                _requestArchive.Add(message);

                return result;
            }

            // Code should be unreachable, returning an error if somehow none of the conditions above were met
            return Result.Fail($"Unable to determine if request ID# {messageId} is valid");
        }

        async Task<Result> ProcessRequestAsync(ServerRequest request)
        {
            IsIdle = false;

            EventOccurred?.Invoke(this, new ServerEvent
            {
                EventType = EventType.ProcessRequestStarted,
                RequestType = request.Type,
                RequestId = request.Id
            });

            var result = Result.Ok();

            switch (request.Type)
            {
                case RequestType.TextMessage:
                    ReceiveTextMessage(request);
                    break;

                case RequestType.InboundFileTransferRequest:
                    result = await HandleInboundFileTransferRequestAsync(request, _token).ConfigureAwait(false);
                    break;

                case RequestType.OutboundFileTransferRequest:
                    result = await HandleOutboundFileTransferRequestAsync(request).ConfigureAwait(false);
                    break;

                case RequestType.FileTransferAccepted:
                    result = await HandleFileTransferAcceptedAsync(request, _token).ConfigureAwait(false);
                    break;

                case RequestType.FileTransferComplete:
                    result = HandleFileTransferCompleted(request);
                    break;

                case RequestType.FileTransferStalled:
                    result = HandleStalledFileTransfer(request);
                    break;

                case RequestType.RetryOutboundFileTransfer:
                    result = await HandleRetryFileTransferAsync(request).ConfigureAwait(false);
                    break;

                case RequestType.RetryLimitExceeded:
                    result = HandleRetryLimitExceeded(request);
                    break;

                case RequestType.FileListRequest:
                    result = await SendFileListAsync(request).ConfigureAwait(false);
                    break;

                case RequestType.FileListResponse:
                    ReceiveFileList(request);
                    break;

                case RequestType.NoFilesAvailableForDownload:
                    HandleNoFilesAvailableForDownload(request);
                    break;

                case RequestType.FileTransferRejected:
                    result = HandleFileTransferRejected(request);
                    break;

                case RequestType.RequestedFolderDoesNotExist:
                    HandleRequestedFolderDoesNotExist(request);
                    break;

                case RequestType.RequestedFileDoesNotExist:
                    result = HandleRequestedFileDoesNotExist(request);
                    break;

                case RequestType.ServerInfoRequest:
                    result = await SendServerInfoAsync(request).ConfigureAwait(false);
                    break;

                case RequestType.ServerInfoResponse:
                    ReceiveServerInfo(request);
                    break;

                case RequestType.ShutdownServerCommand:
                    HandleShutdownServerCommand(request);
                    break;

                default:
                    var error = $"Unable to determine transfer type, value of '{request.Type}' is invalid.";
                    return Result.Fail(error);
            }

            try
            {
                request.Socket.Shutdown(SocketShutdown.Both);
                request.Socket.Close();
            }
            catch (SocketException ex)
            {
                var errorMessage = $"{ex.Message} ({ex.GetType()} raised in method TplSocketServer.ProcessRequestAsync)";
                result = Result.Fail(errorMessage);
            }

            if (result.Failure)
            {
                EventOccurred?.Invoke(this, new ServerEvent
                {
                    EventType = EventType.ErrorOccurred,
                    ErrorMessage = result.Error
                });
            }

            EventOccurred?.Invoke(this, new ServerEvent
            {
                EventType = EventType.ProcessRequestComplete,
                RequestType = request.Type,
                RequestId = request.Id,
                RemoteServerIpAddress = _ipAddressLastConnection
            });

            var handleQueuedRequests = await HandleRemainingRequestsInQueue();
            if (handleQueuedRequests.Failure)
            {
                IsIdle = true;
                return handleQueuedRequests;
            }

            IsIdle = true;
            return result;
        }

        async Task<Result> HandleRemainingRequestsInQueue()
        {
            foreach (var request in _requestQueue)
            {
                if (!request.ProcessRequestImmediately()) continue;

                var result = await ProcessRequestAsync(request.Id);
                if (result.Failure)
                {
                    return result;
                }
            }

            return Result.Ok();
        }

        public async Task<Result> SendTextMessageAsync(
            string message,
            string remoteServerIpAddress,
            int remoteServerPort)
        {
            if (string.IsNullOrEmpty(message))
            {
                return Result.Fail("Message is null or empty string.");
            }

            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.SendTextMessageStarted,
                    TextMessage = message,
                    RemoteServerIpAddress = RemoteServerSessionIpAddress,
                    RemoteServerPortNumber = RemoteServerPortNumber
                });

            var connectResult = await ConnectToServerAsync().ConfigureAwait(false);
            if (connectResult.Failure)
            {
                return connectResult;
            }

            var socket = connectResult.Value;
            var messageData =
                RequestWrapper.ConstructRequestWithStringValue(
                    RequestType.TextMessage,
                    MyLocalIpAddress.ToString(),
                    MyServerPortNumber,
                    message);

            var sendMessageDataResult = await SendMessageData(socket, messageData).ConfigureAwait(false);
            if (sendMessageDataResult.Failure)
            {
                return sendMessageDataResult;
            }

            CloseSocket(socket);

            EventOccurred?.Invoke(this,
                new ServerEvent
                    {EventType = EventType.SendTextMessageComplete});

            return Result.Ok();
        }

        void ReceiveTextMessage(ServerRequest request)
        {
            var (remoteServerIpAddress,
                remoteServerPort,
                textMessage) = RequestUnwrapper.ReadRequestWithStringValue(request.Data);

            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);

            _eventLog.Add(new ServerEvent
            {
                EventType = EventType.ReceivedTextMessage,
                TextMessage = textMessage,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                RequestId = request.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());
        }

        public async Task<Result> SendFileAsync(
            IPAddress remoteServerIpAddress,
            int remoteServerPort,
            string localFilePath,
            string remoteFolderPath)
        {
            if (!File.Exists(localFilePath))
            {
                return Result.Fail("File does not exist: " + localFilePath);
            }

            var outboundFileTransfer = new FileTransfer
            {
                Id = _fileTransferId,
                TransferDirection = FileTransferDirection.Outbound,
                Initiator = FileTransferInitiator.Self,
                Status = FileTransferStatus.AwaitingResponse,
                TransferResponseCode = DateTime.Now.Ticks,
                MyLocalIpAddress = MyLocalIpAddress,
                MyPublicIpAddress = MyPublicIpAddress,
                MyServerPortNumber = MyServerPortNumber,
                RemoteServerIpAddress = remoteServerIpAddress,
                RemoteServerPortNumber = remoteServerPort,
                LocalFilePath = localFilePath,
                LocalFolderPath = Path.GetDirectoryName(localFilePath),
                RemoteFolderPath = remoteFolderPath,
                RemoteFilePath = Path.Combine(remoteFolderPath, Path.GetFileName(localFilePath)),
                FileSizeInBytes = new FileInfo(localFilePath).Length,
                RequestInitiatedTime = DateTime.Now
            };

            _fileTransfers.Add(outboundFileTransfer);
            _fileTransferId++;

            var sendRequestResult = await SendOutboundFileTransferRequestAsync(outboundFileTransfer).ConfigureAwait(false);
            if (sendRequestResult.Success) return Result.Ok();

            outboundFileTransfer.Status = FileTransferStatus.Error;
            outboundFileTransfer.ErrorMessage = sendRequestResult.Error;

            return sendRequestResult;
        }

        async Task<Result> HandleOutboundFileTransferRequestAsync(ServerRequest request)
        {
            var (remoteServerTransferId,
                requestedFilePath,
                remoteServerIpAddress,
                remoteServerPort,
                remoteFolderPath) = RequestUnwrapper.ReadOutboundFileTransferRequest(request.Data);

            if (!File.Exists(requestedFilePath))
            {
                return await SendFileTransferResponse(
                    RequestType.RequestedFileDoesNotExist,
                    0,
                    remoteServerTransferId,
                    EventType.SendNotificationFileDoesNotExistStarted,
                    EventType.SendNotificationFileDoesNotExistComplete).ConfigureAwait(false);
            }

            // TODO: Create logic to check stalled file transfers that are under lockout and if this request matches remoteserver info + localfilepath, send a new filetranserresponse = rejected_retrylimitexceeded. maybe we should penalize them for trying to subvert our lockout policy?

            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);
            RemoteServerTransferFolderPath = remoteFolderPath;

            var outboundFileTransfer = new FileTransfer
            {
                Id = _fileTransferId,
                RemoteServerTransferId = remoteServerTransferId,
                TransferDirection = FileTransferDirection.Outbound,
                Initiator = FileTransferInitiator.RemoteServer,
                Status = FileTransferStatus.AwaitingResponse,
                TransferResponseCode = DateTime.Now.Ticks,
                MyLocalIpAddress = MyLocalIpAddress,
                MyPublicIpAddress = MyPublicIpAddress,
                MyServerPortNumber = MyServerPortNumber,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                LocalFilePath = requestedFilePath,
                LocalFolderPath = Path.GetDirectoryName(requestedFilePath),
                RemoteFolderPath = remoteFolderPath,
                RemoteFilePath = Path.Combine(remoteFolderPath, Path.GetFileName(requestedFilePath)),
                FileSizeInBytes = new FileInfo(requestedFilePath).Length,
                RequestInitiatedTime = DateTime.Now
            };

            _fileTransfers.Add(outboundFileTransfer);
            _fileTransferId++;

            _eventLog.Add(new ServerEvent
            {
                EventType = EventType.ReceivedOutboundFileTransferRequest,
                LocalFolder = Path.GetDirectoryName(requestedFilePath),
                FileName = Path.GetFileName(requestedFilePath),
                FileSizeInBytes = new FileInfo(requestedFilePath).Length,
                RemoteFolder = RemoteServerTransferFolderPath,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                RequestId = request.Id,
                FileTransferId = outboundFileTransfer.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            var sendRequestResult = await SendOutboundFileTransferRequestAsync(outboundFileTransfer).ConfigureAwait(false);
            if (sendRequestResult.Success) return Result.Ok();

            outboundFileTransfer.Status = FileTransferStatus.Error;
            outboundFileTransfer.ErrorMessage = sendRequestResult.Error;

            return sendRequestResult;
        }

        async Task<Result> SendOutboundFileTransferRequestAsync(FileTransfer outboundFileTransfer)
        {
            RemoteServerInfo
                = new ServerInfo(
                    outboundFileTransfer.RemoteServerIpAddress,
                    outboundFileTransfer.RemoteServerPortNumber);

            RemoteServerTransferFolderPath = outboundFileTransfer.RemoteFolderPath;
            RemoteServerAcceptedFileTransfer = false;
            RemoteServerRejectedFileTransfer = false;

            _eventLog.Add(new ServerEvent
            {
                EventType = EventType.RequestOutboundFileTransferStarted,
                LocalIpAddress = MyLocalIpAddress,
                LocalPortNumber = MyServerPortNumber,
                LocalFolder = outboundFileTransfer.LocalFolderPath,
                FileName = Path.GetFileName(outboundFileTransfer.LocalFilePath),
                FileSizeInBytes = outboundFileTransfer.FileSizeInBytes,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                RemoteFolder = RemoteServerTransferFolderPath,
                FileTransferId = outboundFileTransfer.Id
            });

            var messageData =
                RequestWrapper.ConstructOutboundFileTransferRequest(
                    MyLocalIpAddress.ToString(),
                    MyServerPortNumber,
                    outboundFileTransfer);

            EventOccurred?.Invoke(this, _eventLog.Last());

            var connectResult = await ConnectToServerAsync().ConfigureAwait(false);
            if (connectResult.Failure)
            {
                return connectResult;
            }

            outboundFileTransfer.SendSocket = connectResult.Value;
            var sendMessageDataResult = await SendMessageData(outboundFileTransfer.SendSocket, messageData).ConfigureAwait(false);
            if (sendMessageDataResult.Failure)
            {
                return sendMessageDataResult;
            }

            _eventLog.Add(new ServerEvent
            {
                EventType = EventType.RequestOutboundFileTransferComplete,
                LocalIpAddress = MyLocalIpAddress,
                LocalPortNumber = MyServerPortNumber,
                LocalFolder = outboundFileTransfer.LocalFolderPath,
                FileName = Path.GetFileName(outboundFileTransfer.LocalFilePath),
                FileSizeInBytes = outboundFileTransfer.FileSizeInBytes,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                RemoteFolder = RemoteServerTransferFolderPath,
                FileTransferId = outboundFileTransfer.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            return Result.Ok();
        }
        
        Result HandleFileTransferRejected(ServerRequest request)
        {
            (string remoteServerIpAddress,
                int remoteServerPort,
                long responseCode) = RequestUnwrapper.ReadRequestWithInt64Value(request.Data);

            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);
            RemoteServerRejectedFileTransfer = true;

            var getFileTransferResult = GetFileTransferByResponseCode(responseCode);
            if (getFileTransferResult.Failure)
            {
                return getFileTransferResult;
            }

            var outboundFileTransfer = getFileTransferResult.Value;
            outboundFileTransfer.Status = FileTransferStatus.Rejected;
            outboundFileTransfer.TransferStartTime = DateTime.Now;
            outboundFileTransfer.TransferCompleteTime = outboundFileTransfer.TransferStartTime;
            
            //TODO: Investigate why this causes a bug that fails my unit tests
            //OutgoingFilePath = string.Empty;
            //RemoteServerTransferFolderPath = string.Empty;

            _eventLog.Add(new ServerEvent
            {
                EventType = EventType.RemoteServerRejectedFileTransfer,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                RequestId = request.Id,
                FileTransferId = outboundFileTransfer.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());
            TransferInProgress = false;

            CloseSocket(outboundFileTransfer.SendSocket);

            return Result.Ok();
        }

        async Task<Result> HandleFileTransferAcceptedAsync(ServerRequest request, CancellationToken token)
        {
            (string remoteServerIpAddress,
                int remoteServerPort,
                long responseCode) = RequestUnwrapper.ReadRequestWithInt64Value(request.Data);

            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);
            RemoteServerAcceptedFileTransfer = true;
            TransferInProgress = true;

            var getFileTransferResult = GetFileTransferByResponseCode(responseCode);
            if (getFileTransferResult.Failure)
            {
                return getFileTransferResult;
            }

            var outboundFileTransfer = getFileTransferResult.Value;
            outboundFileTransfer.Status = FileTransferStatus.Accepted;
            
            _eventLog.Add(new ServerEvent
            {
                EventType = EventType.RemoteServerAcceptedFileTransfer,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                RequestId = request.Id,
                FileTransferId = outboundFileTransfer.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            var sendFileBytesResult = await SendFileBytesAsync(outboundFileTransfer, request.Id, token);
            if (sendFileBytesResult.Success) return Result.Ok();

            outboundFileTransfer.Status = FileTransferStatus.Error;
            outboundFileTransfer.ErrorMessage = sendFileBytesResult.Error;
            outboundFileTransfer.TransferCompleteTime = DateTime.Now;

            CloseSocket(outboundFileTransfer.SendSocket);
            TransferInProgress = false;

            return sendFileBytesResult;
        }

        async Task<Result> SendFileBytesAsync(
            FileTransfer fileTransfer,
            int requestId,
            CancellationToken token)
        {
            fileTransfer.Status = FileTransferStatus.InProgress;
            fileTransfer.TransferStartTime = DateTime.Now;

            _eventLog.Add(new ServerEvent
            {
                EventType = EventType.SendFileBytesStarted,
                RemoteServerIpAddress = fileTransfer.RemoteServerIpAddress,
                RemoteServerPortNumber = fileTransfer.RemoteServerPortNumber,
                RequestId = requestId,
                FileTransferId = fileTransfer.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            fileTransfer.BytesRemaining = fileTransfer.FileSizeInBytes;
            fileTransfer.FileChunkSentCount = 0;
            OutboundFileTransferStalled = false;

            using (var file = File.OpenRead(fileTransfer.LocalFilePath))
            {
                while (fileTransfer.BytesRemaining > 0)
                {
                    if (token.IsCancellationRequested)
                    {
                        fileTransfer.Status = FileTransferStatus.Cancelled;
                        fileTransfer.TransferCompleteTime = DateTime.Now;
                        fileTransfer.ErrorMessage = "Cancellation requested";

                        return Result.Ok();
                    }

                    var fileChunkSize = (int) Math.Min(BufferSize, fileTransfer.BytesRemaining);
                    _state.Buffer = new byte[fileChunkSize];

                    var numberOfBytesToSend = file.Read(_state.Buffer, 0, fileChunkSize);
                    fileTransfer.BytesRemaining -= numberOfBytesToSend;

                    var offset = 0;
                    var socketSendCount = 0;
                    while (numberOfBytesToSend > 0)
                    {
                        var sendFileChunkResult =
                            await fileTransfer.SendSocket.SendWithTimeoutAsync(
                                _state.Buffer,
                                offset,
                                fileChunkSize,
                                SocketFlags.None,
                                SocketTimeoutInMilliseconds).ConfigureAwait(false);

                        if (OutboundFileTransferStalled)
                        {
                            const string fileTransferStalledErrorMessage =
                                "Aborting file transfer, client says that data is no longer being received (SendFileBytesAsync)";

                            fileTransfer.Status = FileTransferStatus.Cancelled;
                            fileTransfer.TransferCompleteTime = DateTime.Now;
                            fileTransfer.ErrorMessage = fileTransferStalledErrorMessage;

                            return Result.Ok();
                        }

                        if (sendFileChunkResult.Failure)
                        {
                            return sendFileChunkResult;
                        }

                        fileTransfer.CurrentBytesSent = sendFileChunkResult.Value;
                        numberOfBytesToSend -= fileTransfer.CurrentBytesSent;
                        offset += fileTransfer.CurrentBytesSent;
                        socketSendCount++;
                    }

                    fileTransfer.FileChunkSentCount++;

                    var percentRemaining = fileTransfer.BytesRemaining / (float)fileTransfer.FileSizeInBytes;

                    fileTransfer.PercentComplete = 1 - percentRemaining;
                    fileTransfer.CurrentBytesSent = fileChunkSize;
                    
                    if (fileTransfer.FileSizeInBytes > 10 * BufferSize) continue;
                    SocketEventOccurred?.Invoke(this,
                        new ServerEvent
                        {
                            EventType = EventType.SentFileChunkToClient,
                            FileSizeInBytes = fileTransfer.FileSizeInBytes,
                            CurrentFileBytesSent = fileChunkSize,
                            FileBytesRemaining = fileTransfer.BytesRemaining,
                            FileChunkSentCount = fileTransfer.FileChunkSentCount,
                            SocketSendCount = socketSendCount
                        });
                }

                fileTransfer.Status = FileTransferStatus.AwaitingConfirmation;
                fileTransfer.TransferCompleteTime = DateTime.Now;
                fileTransfer.PercentComplete = 1;
                fileTransfer.CurrentBytesSent = 0;
                fileTransfer.BytesRemaining = 0;

                _eventLog.Add(new ServerEvent
                {
                    EventType = EventType.SendFileBytesComplete,
                    RemoteServerIpAddress = fileTransfer.RemoteServerIpAddress,
                    RemoteServerPortNumber = fileTransfer.RemoteServerPortNumber,
                    RequestId = requestId,
                    FileTransferId = fileTransfer.Id
                });

                EventOccurred?.Invoke(this, _eventLog.Last());

                CloseSocket(fileTransfer.SendSocket);
                TransferInProgress = false;

                return Result.Ok();
            }
        }

        Result HandleFileTransferCompleted(ServerRequest request)
        {
            (string remoteServerIpAddress,
                int remoteServerPort,
                long responseCode) = RequestUnwrapper.ReadRequestWithInt64Value(request.Data);

            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);

            var getFileTransferResult = GetFileTransferByResponseCode(responseCode);
            if (getFileTransferResult.Failure)
            {
                return getFileTransferResult;
            }

            var outboundFileTransfer = getFileTransferResult.Value;
            outboundFileTransfer.Status = FileTransferStatus.Complete;

            _eventLog.Add(new ServerEvent
            {
                EventType = EventType.RemoteServerConfirmedFileTransferCompleted,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                RequestId = request.Id,
                FileTransferId = outboundFileTransfer.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            return Result.Ok();
        }
        
        public async Task<Result> GetFileAsync(
            string remoteServerIpAddress,
            int remoteServerPort,
            string remoteFilePath,
            string localFolderPath)
        {
            var inboundFileTransfer = new FileTransfer
            {
                Id = _fileTransferId,
                TransferDirection = FileTransferDirection.Inbound,
                Initiator = FileTransferInitiator.Self,
                Status = FileTransferStatus.AwaitingResponse,
                MyLocalIpAddress = MyLocalIpAddress,
                MyPublicIpAddress = MyPublicIpAddress,
                MyServerPortNumber = MyServerPortNumber,
                RemoteServerIpAddress = NetworkUtilities.ParseSingleIPv4Address(remoteServerIpAddress).Value,
                RemoteServerPortNumber = remoteServerPort,
                LocalFilePath = Path.Combine(localFolderPath, Path.GetFileName(remoteFilePath)),
                LocalFolderPath = localFolderPath,
                RemoteFolderPath = Path.GetDirectoryName(remoteFilePath),
                RemoteFilePath = remoteFilePath,
                RequestInitiatedTime = DateTime.Now
            };

            _fileTransfers.Add(inboundFileTransfer);
            _fileTransferId++;

            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);
            RemoteFilePath = remoteFilePath;
            RemoteServerTransferFolderPath = Path.GetDirectoryName(remoteFilePath);
            MyTransferFolderPath = localFolderPath;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.RequestInboundFileTransferStarted,
                    RemoteServerIpAddress = RemoteServerSessionIpAddress,
                    RemoteServerPortNumber = RemoteServerPortNumber,
                    RemoteFolder = RemoteServerTransferFolderPath,
                    FileName = Path.GetFileName(RemoteFilePath),
                    LocalIpAddress = MyLocalIpAddress,
                    LocalPortNumber = MyServerPortNumber,
                    LocalFolder = MyTransferFolderPath,
                    FileTransferId = inboundFileTransfer.Id
                });

            var messageData =
                RequestWrapper.ConstructInboundFileTransferRequest(
                    MyLocalIpAddress.ToString(),
                    MyServerPortNumber,
                    inboundFileTransfer);

            var connectResult = await ConnectToServerAsync().ConfigureAwait(false);
            if (connectResult.Failure)
            {
                inboundFileTransfer.Status = FileTransferStatus.Error;
                inboundFileTransfer.ErrorMessage = connectResult.Error;

                return connectResult;
            }

            var socket = connectResult.Value;
            var sendMessageDataResult = await SendMessageData(socket, messageData).ConfigureAwait(false);
            if (sendMessageDataResult.Failure)
            {
                inboundFileTransfer.Status = FileTransferStatus.Error;
                inboundFileTransfer.ErrorMessage = connectResult.Error;

                return sendMessageDataResult;
            }

            CloseSocket(socket);

            EventOccurred?.Invoke(this,
                new ServerEvent
                    {EventType = EventType.RequestInboundFileTransferComplete});

            return Result.Ok();
        }

        Result HandleRequestedFileDoesNotExist(ServerRequest request)
        {
            (string remoteServerIpAddress,
                int remoteServerPort,
                long remoteServerTransferId) = RequestUnwrapper.ReadRequestWithInt64Value(request.Data);

            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);

            var getFileTransferResult = GetFileTransferById(Convert.ToInt32(remoteServerTransferId));
            if (getFileTransferResult.Failure)
            {
                return getFileTransferResult;
            }

            var inboundFileTransfer = getFileTransferResult.Value;
            inboundFileTransfer.Status = FileTransferStatus.Rejected;
            inboundFileTransfer.TransferStartTime = DateTime.Now;
            inboundFileTransfer.TransferCompleteTime = inboundFileTransfer.TransferStartTime;

            _eventLog.Add(new ServerEvent
            {
                EventType = EventType.ReceivedNotificationFileDoesNotExist,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                RequestId = request.Id,
                FileTransferId = inboundFileTransfer.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());
            TransferInProgress = false;

            return Result.Ok();
        }

        async Task<Result> HandleInboundFileTransferRequestAsync(ServerRequest request, CancellationToken token)
        {
            var (responseCode,
                transferId,
                retryCounter,
                retryLimit,
                localFilePath,
                fileSizeBytes,
                remoteServerIpAddress,
                remoteServerPort) = RequestUnwrapper.ReadInboundFileTransferRequest(request.Data);

            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);
            IncomingFilePath = localFilePath;
            _state.IncomingFileSize = fileSizeBytes;

            var getFileTransferResult =
                GetFileTransfer(
                    request.Socket,
                    transferId,
                    responseCode,
                    retryCounter,
                    retryLimit,
                    localFilePath,
                    fileSizeBytes,
                    remoteServerIpAddress,
                    remoteServerPort);

            if (getFileTransferResult.Failure)
            {
                return Result.Fail(getFileTransferResult.Error);
            }

            var inboundFileTransfer = getFileTransferResult.Value;

            _eventLog.Add(new ServerEvent
            {
                EventType = EventType.ReceivedInboundFileTransferRequest,
                LocalFolder = inboundFileTransfer.LocalFolderPath,
                FileName = Path.GetFileName(inboundFileTransfer.LocalFilePath),
                FileSizeInBytes = inboundFileTransfer.FileSizeInBytes,
                RemoteServerIpAddress = inboundFileTransfer.RemoteServerIpAddress,
                RemoteServerPortNumber = inboundFileTransfer.RemoteServerPortNumber,
                RetryCounter = inboundFileTransfer.RetryCounter,
                RemoteServerRetryLimit =  inboundFileTransfer.RemoteServerRetryLimit,
                RequestId = request.Id,
                FileTransferId = inboundFileTransfer.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            if (File.Exists(inboundFileTransfer.LocalFilePath))
            {
                inboundFileTransfer.Status = FileTransferStatus.Rejected;
                inboundFileTransfer.TransferStartTime = DateTime.Now;
                inboundFileTransfer.TransferCompleteTime = inboundFileTransfer.TransferStartTime;

                return await SendFileTransferResponse(
                        RequestType.FileTransferRejected,
                        inboundFileTransfer.Id,
                        inboundFileTransfer.TransferResponseCode,
                        EventType.SendFileTransferRejectedStarted,
                        EventType.SendFileTransferRejectedComplete).ConfigureAwait(false);
            }

            inboundFileTransfer.Status = FileTransferStatus.Accepted;

            var acceptTransferResult =
                await SendFileTransferResponse(
                    RequestType.FileTransferAccepted,
                    inboundFileTransfer.Id,
                    inboundFileTransfer.TransferResponseCode,
                    EventType.SendFileTransferAcceptedStarted,
                    EventType.SendFileTransferAcceptedComplete).ConfigureAwait(false);

            if (acceptTransferResult.Failure)
            {
                inboundFileTransfer.Status = FileTransferStatus.Error;
                inboundFileTransfer.ErrorMessage = acceptTransferResult.Error;

                return acceptTransferResult;
            }

            var receiveFileResult = await ReceiveFileAsync(request, inboundFileTransfer, token).ConfigureAwait(false);
            if (receiveFileResult.Success) return Result.Ok();

            inboundFileTransfer.Status = FileTransferStatus.Error;
            inboundFileTransfer.ErrorMessage = receiveFileResult.Error;

            return receiveFileResult;
        }

        Result<FileTransfer> GetFileTransfer(
            Socket socket,
            int fileTransferId,
            long responseCode,
            int retryCounter,
            int retryLimit,
            string localFilePath,
            long fileSizeInBytes,
            string remoteServerIpAddress,
            int remoteServerPort)
        {
            var getFileTransferResult = GetFileTransferById(fileTransferId);

            if (fileTransferId == 0 || getFileTransferResult.Failure)
            {
                var newInboundFileTransfer = new FileTransfer
                {
                    Id = _fileTransferId,
                    TransferDirection = FileTransferDirection.Inbound,
                    Initiator = FileTransferInitiator.RemoteServer,
                    Status = FileTransferStatus.AwaitingResponse,
                    TransferResponseCode = responseCode,
                    RemoteServerRetryLimit = retryLimit,
                    MyLocalIpAddress = MyLocalIpAddress,
                    MyPublicIpAddress = MyPublicIpAddress,
                    MyServerPortNumber = MyServerPortNumber,
                    RemoteServerIpAddress = NetworkUtilities.ParseSingleIPv4Address(remoteServerIpAddress).Value,
                    RemoteServerPortNumber = remoteServerPort,
                    LocalFilePath = localFilePath,
                    LocalFolderPath = Path.GetDirectoryName(localFilePath),
                    FileSizeInBytes = fileSizeInBytes,
                    RequestInitiatedTime = DateTime.Now,
                    ReceiveSocket = socket
                };

                _fileTransfers.Add(newInboundFileTransfer);
                _fileTransferId++;

                return Result.Ok(newInboundFileTransfer);
            }

            var inboundFileTransfer = getFileTransferResult.Value;
            inboundFileTransfer.ReceiveSocket = socket;
            inboundFileTransfer.TransferResponseCode = responseCode;
            inboundFileTransfer.FileSizeInBytes = fileSizeInBytes;
            inboundFileTransfer.RemoteServerRetryLimit = retryLimit;

            if (retryCounter == 1) return Result.Ok(inboundFileTransfer);

            inboundFileTransfer.RetryCounter = retryCounter;
            inboundFileTransfer.Status = FileTransferStatus.AwaitingResponse;
            inboundFileTransfer.ErrorMessage = string.Empty;

            inboundFileTransfer.RequestInitiatedTime = DateTime.Now;
            inboundFileTransfer.TransferStartTime = DateTime.MinValue;
            inboundFileTransfer.TransferCompleteTime = DateTime.MinValue;

            inboundFileTransfer.CurrentBytesReceived = 0;
            inboundFileTransfer.TotalBytesReceived = 0;
            inboundFileTransfer.BytesRemaining = 0;
            inboundFileTransfer.PercentComplete = 0;

            return Result.Ok(inboundFileTransfer);
        }

        async Task<Result> ReceiveFileAsync(
            ServerRequest request,
            FileTransfer fileTransfer,
            CancellationToken token)
        {
            fileTransfer.Status = FileTransferStatus.InProgress;
            fileTransfer.TransferStartTime = DateTime.Now;

            _eventLog.Add(new ServerEvent
            {
                EventType = EventType.ReceiveFileBytesStarted,
                RemoteServerIpAddress = fileTransfer.RemoteServerIpAddress,
                RemoteServerPortNumber = fileTransfer.RemoteServerPortNumber,
                FileTransferStartTime = fileTransfer.TransferStartTime,
                FileSizeInBytes = fileTransfer.FileSizeInBytes,
                RequestId = request.Id,
                FileTransferId = fileTransfer.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            var receiveCount = 0;
            fileTransfer.TotalBytesReceived = 0;
            fileTransfer.BytesRemaining = fileTransfer.FileSizeInBytes;
            fileTransfer.PercentComplete = 0;
            InboundFileTransferStalled = false;

            if (_state.UnreadBytes.Count > 0)
            {
                fileTransfer.TotalBytesReceived += _state.UnreadBytes.Count;
                fileTransfer.BytesRemaining -= _state.UnreadBytes.Count;

                lock (FileLock)
                {
                    var writeBytesResult =
                        FileHelper.WriteBytesToFile(
                            fileTransfer.LocalFilePath,
                            _state.UnreadBytes.ToArray(),
                            _state.UnreadBytes.Count,
                            10);

                    if (writeBytesResult.Failure)
                    {
                        return writeBytesResult;
                    }
                }

                _eventLog.Add(new ServerEvent
                {
                    EventType = EventType.CopySavedBytesToIncomingFile,
                    CurrentFileBytesReceived = _state.UnreadBytes.Count,
                    TotalFileBytesReceived = fileTransfer.TotalBytesReceived,
                    FileSizeInBytes = fileTransfer.FileSizeInBytes,
                    FileBytesRemaining = fileTransfer.BytesRemaining
                });

                EventOccurred?.Invoke(this, _eventLog.Last());
            }

            // Read file bytes from transfer socket until 
            //      1. the entire file has been received OR 
            //      2. Data is no longer being received OR
            //      3, Transfer is canceled
            while (fileTransfer.BytesRemaining > 0)
            {
                if (token.IsCancellationRequested)
                {
                    fileTransfer.Status = FileTransferStatus.Cancelled;
                    fileTransfer.TransferCompleteTime = DateTime.Now;
                    fileTransfer.ErrorMessage = "Cancellation requested";

                    return Result.Ok();
                }

                var readFromSocketResult = await ReadFromSocketAsync(fileTransfer.ReceiveSocket).ConfigureAwait(false);
                if (readFromSocketResult.Failure)
                {
                    return Result.Fail(readFromSocketResult.Error);
                }

                fileTransfer.CurrentBytesReceived = readFromSocketResult.Value;
                var receivedBytes = new byte[fileTransfer.CurrentBytesReceived];

                if (fileTransfer.CurrentBytesReceived == 0)
                {
                    return Result.Fail("Socket is no longer receiving data, must abort file transfer");
                }

                int fileWriteAttempts;
                lock (FileLock)
                {
                    var writeBytesResult = FileHelper.WriteBytesToFile(
                        fileTransfer.LocalFilePath,
                        receivedBytes,
                        fileTransfer.CurrentBytesReceived,
                        10);

                    if (writeBytesResult.Failure)
                    {
                        return writeBytesResult;
                    }

                    fileWriteAttempts = writeBytesResult.Value + 1;
                }
                
                receiveCount++;
                fileTransfer.TotalBytesReceived += fileTransfer.CurrentBytesReceived;
                fileTransfer.BytesRemaining -= fileTransfer.CurrentBytesReceived;
                var checkPercentComplete = fileTransfer.TotalBytesReceived / (float) fileTransfer.FileSizeInBytes;
                var changeSinceLastUpdate = checkPercentComplete - fileTransfer.PercentComplete;

                if (fileWriteAttempts > 1)
                {
                    _eventLog.Add(new ServerEvent
                    {
                        EventType = EventType.MultipleFileWriteAttemptsNeeded,
                        FileWriteAttempts = fileWriteAttempts,
                        PercentComplete = fileTransfer.PercentComplete,
                        FileTransferId = fileTransfer.Id
                    });

                    EventOccurred?.Invoke(this, _eventLog.Last());
                }

                // this method fires on every socket read event, which could be hurdreds of thousands
                // of times depending on the file size and buffer size. Since this  event is only used
                // by myself when debugging small test files, I limited this event to only fire when 
                // the size of the file will result in less than 10 read events
                if (fileTransfer.FileSizeInBytes < 10 * BufferSize)
                {
                    SocketEventOccurred?.Invoke(this,
                    new ServerEvent
                    {
                        EventType = EventType.ReceivedFileBytesFromSocket,
                        SocketReadCount = receiveCount,
                        BytesReceived = fileTransfer.CurrentBytesReceived,
                        CurrentFileBytesReceived = fileTransfer.CurrentBytesReceived,
                        TotalFileBytesReceived = fileTransfer.TotalBytesReceived,
                        FileSizeInBytes = fileTransfer.FileSizeInBytes,
                        FileBytesRemaining = fileTransfer.BytesRemaining,
                        PercentComplete = fileTransfer.PercentComplete
                    });
                }

                // Report progress in intervals which are set by the user in the settings file
                if (changeSinceLastUpdate < TransferUpdateInterval) continue;

                fileTransfer.PercentComplete = checkPercentComplete;

                FileTransferProgress?.Invoke(this, new ServerEvent
                {
                    EventType = EventType.UpdateFileTransferProgress,
                    TotalFileBytesReceived = fileTransfer.TotalBytesReceived,
                    PercentComplete = fileTransfer.PercentComplete,
                    RequestId = request.Id
                });
            }

            if (InboundFileTransferStalled)
            {
                const string fileTransferStalledErrorMessage =
                    "Data is no longer bring received from remote client, file transfer has been canceled (ReceiveFileAsync)";

                fileTransfer.Status = FileTransferStatus.Stalled;
                fileTransfer.TransferCompleteTime = DateTime.Now;
                fileTransfer.ErrorMessage = fileTransferStalledErrorMessage;

                return Result.Ok();
            }
            
            fileTransfer.Status = FileTransferStatus.Complete;
            fileTransfer.TransferCompleteTime = DateTime.Now;
            fileTransfer.PercentComplete = 1;
            fileTransfer.CurrentBytesReceived = 0;
            fileTransfer.BytesRemaining = 0;

            _eventLog.Add(new ServerEvent
            {
                EventType = EventType.ReceiveFileBytesComplete,
                FileTransferStartTime = fileTransfer.TransferStartTime,
                FileTransferCompleteTime = DateTime.Now,
                FileSizeInBytes = fileTransfer.FileSizeInBytes,
                RemoteServerIpAddress = fileTransfer.RemoteServerIpAddress,
                RemoteServerPortNumber = fileTransfer.RemoteServerPortNumber,
                RequestId = request.Id,
                FileTransferId = fileTransfer.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            var confirmFileTransferResult =
                await SendFileTransferResponse(
                    RequestType.FileTransferComplete,
                    fileTransfer.Id,
                    fileTransfer.TransferResponseCode,
                    EventType.SendFileTransferCompletedStarted,
                    EventType.SendFileTransferCompletedCompleted).ConfigureAwait(false);

            if (confirmFileTransferResult.Success) return Result.Ok();

            fileTransfer.Status = FileTransferStatus.Error;
            fileTransfer.ErrorMessage = confirmFileTransferResult.Error;

            return confirmFileTransferResult;
        }

        public async Task<Result> SendNotificationFileTransferStalledAsync(int fileTransferId)
        {
            const string fileTransferStalledErrorMessage =
                "Data is no longer bring received from remote client, file transfer has been canceled (SendNotificationFileTransferStalledAsync)";

            var getFileTransferResult = GetFileTransferById(fileTransferId);
            if (getFileTransferResult.Failure)
            {
                var error = $"{getFileTransferResult.Error} (TplSocketServer.SendNotificationFileTransferStalledAsync)";
                return Result.Fail(error);
            }

            var inboundFileTransfer = getFileTransferResult.Value;
            inboundFileTransfer.Status = FileTransferStatus.Stalled;
            inboundFileTransfer.TransferCompleteTime = DateTime.Now;
            inboundFileTransfer.ErrorMessage = fileTransferStalledErrorMessage;

            InboundFileTransferStalled = true;

            return await
                SendFileTransferResponse(
                    RequestType.FileTransferStalled,
                    inboundFileTransfer.Id,
                    inboundFileTransfer.TransferResponseCode,
                    EventType.SendFileTransferStalledStarted,
                    EventType.SendFileTransferStalledComplete);
        }

        Result HandleStalledFileTransfer(ServerRequest request)
        {
            const string fileTransferStalledErrorMessage =
                "Aborting file transfer, client says that data is no longer being received (HandleStalledFileTransfer)";

            (string remoteServerIpAddress,
                int remoteServerPort,
                long responseCode) = RequestUnwrapper.ReadRequestWithInt64Value(request.Data);

            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);

            var getFileTransferResult = GetFileTransferByResponseCode(responseCode);
            if (getFileTransferResult.Failure)
            {
                return Result.Fail(getFileTransferResult.Error);
            }

            var outboundFileTransfer = getFileTransferResult.Value;
            outboundFileTransfer.Status = FileTransferStatus.Cancelled;
            outboundFileTransfer.TransferCompleteTime = DateTime.Now;
            outboundFileTransfer.ErrorMessage = fileTransferStalledErrorMessage;

            _eventLog.Add(new ServerEvent
            {
                EventType = EventType.FileTransferStalled,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                RequestId = request.Id,
                FileTransferId = outboundFileTransfer.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            OutboundFileTransferStalled = true;
            return Result.Ok();
        }

        public async Task<Result> RetryFileTransferAsync(
            int fileTransferId,
            IPAddress remoteServerIpAddress,
            int remoteServerPort)
        {
            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);

            var getFileTransferResult = GetFileTransferById(fileTransferId);
            if (getFileTransferResult.Failure)
            {
                var error = $"{getFileTransferResult.Error} (TplSocketServer.RetryFileTransferAsync)";
                return Result.Fail(error);
            }

            var stalledFileTransfer = getFileTransferResult.Value;

            return await
                SendFileTransferResponse(
                    RequestType.RetryOutboundFileTransfer,
                    stalledFileTransfer.Id,
                    stalledFileTransfer.TransferResponseCode,
                    EventType.RetryOutboundFileTransferStarted,
                    EventType.RetryOutboundFileTransferComplete);
        }

        async Task<Result> HandleRetryFileTransferAsync(ServerRequest request)
        {
            (string remoteServerIpAddress,
                int remoteServerPort,
                long responseCode) = RequestUnwrapper.ReadRequestWithInt64Value(request.Data);

            EventOccurred?.Invoke(this, _eventLog.Last());

            var getFileTransferResult = GetFileTransferByResponseCode(responseCode);
            if (getFileTransferResult.Failure)
            {
                return Result.Fail(getFileTransferResult.Error);
            }

            var canceledFileTransfer = getFileTransferResult.Value;

            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);

            _eventLog.Add(new ServerEvent
            {
                EventType = EventType.ReceivedRetryOutboundFileTransferRequest,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                RequestId = request.Id,
                FileTransferId = canceledFileTransfer.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            if (canceledFileTransfer.RetryCounter >= TransferRetryLimit)
            {
                var retryLImitExceeded =
                    $"{Environment.NewLine}Maximum # of attempts to complete stalled file transfer reached or exceeded: " +
                    $"({TransferRetryLimit} failed attempts for \"{Path.GetFileName(canceledFileTransfer.LocalFilePath)}\")";
                
                canceledFileTransfer.Status = FileTransferStatus.RetryLimitExceeded;
                canceledFileTransfer.RetryLockoutExpireTime = DateTime.Now + RetryLimitLockout;
                canceledFileTransfer.ErrorMessage = retryLImitExceeded;

                return await SendRetryLimitExceeded(canceledFileTransfer);
            }

            canceledFileTransfer.ResetTransferValues();
            canceledFileTransfer.RemoteServerRetryLimit = TransferRetryLimit;
            
            var sendRequestResult = await SendOutboundFileTransferRequestAsync(canceledFileTransfer).ConfigureAwait(false);
            if (sendRequestResult.Success) return Result.Ok();

            canceledFileTransfer.Status = FileTransferStatus.Error;
            canceledFileTransfer.ErrorMessage = sendRequestResult.Error;

            return sendRequestResult;
        }
        
        async Task<Result> SendRetryLimitExceeded(FileTransfer outboundFileTransfer)
        {
            _eventLog.Add(new ServerEvent
            {
                EventType = EventType.SendRetryLimitExceededStarted,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                LocalIpAddress = MyLocalIpAddress,
                LocalPortNumber = MyServerPortNumber,
                FileName = Path.GetFileName(outboundFileTransfer.LocalFilePath),
                RetryCounter = outboundFileTransfer.RetryCounter,
                RemoteServerRetryLimit = TransferRetryLimit,
                RetryLockoutExpireTime = outboundFileTransfer.RetryLockoutExpireTime,
                FileTransferId = outboundFileTransfer.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            var connectResult = await ConnectToServerAsync().ConfigureAwait(false);
            if (connectResult.Failure)
            {
                return connectResult;
            }

            var socket = connectResult.Value;
            var messageData =
                RequestWrapper.ConstructRetryLimitExceededRequest(
                    MyLocalIpAddress.ToString(),
                    MyServerPortNumber,
                    outboundFileTransfer.RemoteServerTransferId,
                    TransferRetryLimit,
                    outboundFileTransfer.RetryLockoutExpireTime.Ticks);

            var sendMessageDataResult = await SendMessageData(socket, messageData).ConfigureAwait(false);
            if (sendMessageDataResult.Failure)
            {
                return sendMessageDataResult;
            }

            CloseSocket(socket);

            _eventLog.Add(new ServerEvent
            {
                EventType = EventType.SendRetryLimitExceededCompleted,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                LocalIpAddress = MyLocalIpAddress,
                LocalPortNumber = MyServerPortNumber,
                FileTransferId = outboundFileTransfer.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            return Result.Ok();
        }

        Result HandleRetryLimitExceeded(ServerRequest request)
        {
            (string remoteServerIpAddress,
                int remoteServerPort,
                int remoteServerTransferId,
                int retryLimit,
                long lockoutExpireTimeTicks) = RequestUnwrapper.ReadRetryLimitExceededRequest(request.Data);

            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);
            var lockoutExpireTime = new DateTime(lockoutExpireTimeTicks);

            var getFileTransferResult = GetFileTransferById(remoteServerTransferId);
            if (getFileTransferResult.Failure)
            {
                return getFileTransferResult;
            }

            var inboundFileTransfer = getFileTransferResult.Value;
            inboundFileTransfer.Status = FileTransferStatus.RetryLimitExceeded;
            inboundFileTransfer.RetryLockoutExpireTime = lockoutExpireTime;

            _eventLog.Add(new ServerEvent
            {
                EventType = EventType.ReceiveRetryLimitExceeded,
                LocalFolder = inboundFileTransfer.LocalFolderPath,
                FileName = Path.GetFileName(inboundFileTransfer.LocalFilePath),
                FileSizeInBytes = inboundFileTransfer.FileSizeInBytes,
                RemoteServerIpAddress = inboundFileTransfer.RemoteServerIpAddress,
                RemoteServerPortNumber = inboundFileTransfer.RemoteServerPortNumber,
                RetryCounter = inboundFileTransfer.RetryCounter,
                RemoteServerRetryLimit = retryLimit,
                RetryLockoutExpireTime = inboundFileTransfer.RetryLockoutExpireTime,
                RequestId = request.Id,
                FileTransferId = inboundFileTransfer.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            return Result.Ok();
        }

        async Task<Result> SendFileTransferResponse(
            RequestType requestType,
            int fileTransferId,
            long responseCode,
            EventType sendMessageStartedEventType,
            EventType sendMessageCompleteEventType)
        {
            _eventLog.Add(new ServerEvent
            {
                EventType = sendMessageStartedEventType,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                LocalIpAddress = MyLocalIpAddress,
                LocalPortNumber = MyServerPortNumber,
                FileTransferId = fileTransferId
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            var connectResult = await ConnectToServerAsync().ConfigureAwait(false);
            if (connectResult.Failure)
            {
                return connectResult;
            }

            var socket = connectResult.Value;
            var messageData =
                RequestWrapper.ConstructRequestWithInt64Value(
                    requestType,
                    MyLocalIpAddress.ToString(),
                    MyServerPortNumber,
                    responseCode);

            var sendMessageDataResult = await SendMessageData(socket, messageData).ConfigureAwait(false);
            if (sendMessageDataResult.Failure)
            {
                return sendMessageDataResult;
            }

            CloseSocket(socket);

            _eventLog.Add(new ServerEvent
            {
                EventType = sendMessageCompleteEventType,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                LocalIpAddress = MyLocalIpAddress,
                LocalPortNumber = MyServerPortNumber,
                FileTransferId = fileTransferId
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            return Result.Ok();
        }
        
        async Task<Result> SendSimpleMessageToClientAsync(
            RequestType requestType,
            EventType sendMessageStartedEventType,
            EventType sendMessageCompleteEventType)
        {
            _eventLog.Add(new ServerEvent
            {
                EventType = sendMessageStartedEventType,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                LocalIpAddress = MyLocalIpAddress,
                LocalPortNumber = MyServerPortNumber
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            var connectResult = await ConnectToServerAsync().ConfigureAwait(false);
            if (connectResult.Failure)
            {
                return connectResult;
            }

            var socket = connectResult.Value;
            var messageData =
                RequestWrapper.ConstructBasicRequest(
                    requestType,
                    MyLocalIpAddress.ToString(),
                    MyServerPortNumber);

            var sendMessageDataResult = await SendMessageData(socket, messageData).ConfigureAwait(false);
            if (sendMessageDataResult.Failure)
            {
                return sendMessageDataResult;
            }

            CloseSocket(socket);

            _eventLog.Add(new ServerEvent
            {
                EventType = sendMessageCompleteEventType,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                LocalIpAddress = MyLocalIpAddress,
                LocalPortNumber = MyServerPortNumber
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            return Result.Ok();
        }

        async Task<Result<Socket>> ConnectToServerAsync()
        {
            var socket =
                new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ConnectToRemoteServerStarted,
                    RemoteServerIpAddress = RemoteServerSessionIpAddress,
                    RemoteServerPortNumber = RemoteServerPortNumber
                });

            var connectResult =
                await socket.ConnectWithTimeoutAsync(
                    RemoteServerSessionIpAddress,
                    RemoteServerPortNumber,
                    SocketTimeoutInMilliseconds).ConfigureAwait(false);

            if (connectResult.Failure)
            {
                return Result.Fail<Socket>(connectResult.Error);
            }

            EventOccurred?.Invoke(this,
                new ServerEvent
                    {EventType = EventType.ConnectToRemoteServerComplete});

            return Result.Ok(socket);
        }

        async Task<Result> SendMessageData(Socket socket, byte[] messageData)
        {
            var messageLength = BitConverter.GetBytes(messageData.Length);

            var sendMessageLengthResult =
                await socket.SendWithTimeoutAsync(
                    messageLength,
                    0,
                    messageLength.Length,
                    SocketFlags.None,
                    SocketTimeoutInMilliseconds).ConfigureAwait(false);

            if (sendMessageLengthResult.Failure)
            {
                return sendMessageLengthResult;
            }

            var sendMessageResult =
                await socket.SendWithTimeoutAsync(
                    messageData,
                    0,
                    messageData.Length,
                    SocketFlags.None,
                    SocketTimeoutInMilliseconds).ConfigureAwait(false);

            return sendMessageResult.Success
                ? Result.Ok()
                : sendMessageResult;
        }

        void CloseSocket(Socket socket)
        {
            socket.Shutdown(SocketShutdown.Both);
            socket.Close();
        }

        public async Task<Result> RequestFileListAsync(
            IPAddress remoteServerIpAddress,
            int remoteServerPort,
            string targetFolder)
        {
            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);
            RemoteServerTransferFolderPath = targetFolder;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.RequestFileListStarted,
                    LocalIpAddress = MyLocalIpAddress,
                    LocalPortNumber = MyServerPortNumber,
                    RemoteServerIpAddress = RemoteServerSessionIpAddress,
                    RemoteServerPortNumber = RemoteServerPortNumber,
                    RemoteFolder = RemoteServerTransferFolderPath
                });

            var connectResult = await ConnectToServerAsync().ConfigureAwait(false);
            if (connectResult.Failure)
            {
                return connectResult;
            }

            var messageData =
                RequestWrapper.ConstructRequestWithStringValue(
                    RequestType.FileListRequest,
                    MyLocalIpAddress.ToString(),
                    MyServerPortNumber,
                    RemoteServerTransferFolderPath);

            var socket = connectResult.Value;
            var sendMessageDataResult = await SendMessageData(socket, messageData).ConfigureAwait(false);
            if (sendMessageDataResult.Failure)
            {
                return sendMessageDataResult;
            }

            CloseSocket(socket);

            EventOccurred?.Invoke(this,
            new ServerEvent {EventType = EventType.RequestFileListComplete});

            return Result.Ok();
        }

        async Task<Result> SendFileListAsync(ServerRequest request)
        {
            (string remoteServerIpAddress,
                int remoteServerPort,
                string targetFolderPath) = RequestUnwrapper.ReadFileListRequest(request.Data);

            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);
            MyTransferFolderPath = targetFolderPath;

            _eventLog.Add(new ServerEvent
            {
                EventType = EventType.ReceivedFileListRequest,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                RemoteFolder = MyTransferFolderPath,
                RequestId = request.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            if (!Directory.Exists(MyTransferFolderPath))
            {
                return
                    await SendSimpleMessageToClientAsync(
                        RequestType.RequestedFolderDoesNotExist,
                        EventType.SendNotificationFolderDoesNotExistStarted,
                        EventType.SendNotificationFolderDoesNotExistComplete).ConfigureAwait(false);
            }

            var fileInfoList = new FileInfoList(MyTransferFolderPath);
            if (fileInfoList.Count == 0)
            {
                return await SendSimpleMessageToClientAsync(
                    RequestType.NoFilesAvailableForDownload,
                    EventType.SendNotificationNoFilesToDownloadStarted,
                    EventType.SendNotificationNoFilesToDownloadComplete).ConfigureAwait(false);
            }

            _eventLog.Add(new ServerEvent
            {
                EventType = EventType.SendFileListStarted,
                LocalIpAddress = MyLocalIpAddress,
                LocalPortNumber = MyServerPortNumber,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                RemoteServerFileList = fileInfoList,
                LocalFolder = MyTransferFolderPath,
                RequestId = request.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            var connectResult = await ConnectToServerAsync().ConfigureAwait(false);
            if (connectResult.Failure)
            {
                return connectResult;
            }

            var socket = connectResult.Value;
            var responseData =
                RequestWrapper.ConstructFileListResponse(
                    fileInfoList,
                    "*",
                    "|",
                    MyLocalIpAddress.ToString(),
                    MyServerPortNumber,
                    MyTransferFolderPath);

            var sendMessageDataResult = await SendMessageData(socket, responseData).ConfigureAwait(false);
            if (sendMessageDataResult.Failure)
            {
                return sendMessageDataResult;
            }

            CloseSocket(socket);

            _eventLog.Add(new ServerEvent
            {
                EventType = EventType.SendFileListComplete,
                RequestId = request.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            return Result.Ok();
        }

        void HandleRequestedFolderDoesNotExist(ServerRequest request)
        {
            (string remoteServerIpAddress,
                int remoteServerPort) = RequestUnwrapper.ReadServerConnectionInfo(request.Data);

            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);

            _eventLog.Add(new ServerEvent
            {
                EventType = EventType.ReceivedNotificationFolderDoesNotExist,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                RequestId = request.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());
        }

        void HandleNoFilesAvailableForDownload(ServerRequest request)
        {
            (string remoteServerIpAddress,
                int remoteServerPort) = RequestUnwrapper.ReadServerConnectionInfo(request.Data);

            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);

            _eventLog.Add(new ServerEvent
            {
                EventType = EventType.ReceivedNotificationNoFilesToDownload,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                RequestId = request.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());
        }

        void ReceiveFileList(ServerRequest request)
        {
            var (remoteServerIpAddress,
                remoteServerPort,
                transferFolder,
                fileList) = RequestUnwrapper.ReadFileListResponse(request.Data);

            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);
            RemoteServerTransferFolderPath = transferFolder;
            RemoteServerFileList = fileList;

            _eventLog.Add(new ServerEvent
            {
                EventType = EventType.ReceivedFileList,
                LocalIpAddress = MyLocalIpAddress,
                LocalPortNumber = MyServerPortNumber,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                RemoteFolder = RemoteServerTransferFolderPath,
                RemoteServerFileList = RemoteServerFileList,
                RequestId = request.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());
        }

        public Task<Result> RequestServerInfoAsync(IPAddress remoteServerIpAddress, int remoteServerPort)
        {
            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);

            return
                SendSimpleMessageToClientAsync(
                    RequestType.ServerInfoRequest,
                    EventType.RequestServerInfoStarted,
                    EventType.RequestServerInfoComplete);
        }
        
        async Task<Result> SendServerInfoAsync(ServerRequest request)
        {
            (string remoteServerIpAddress,
                int remoteServerPort) = RequestUnwrapper.ReadServerConnectionInfo(request.Data);

            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);

            _eventLog.Add(new ServerEvent
            {
                EventType = EventType.ReceivedServerInfoRequest,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                RequestId = request.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            var publicIpResult = await NetworkUtilities.GetPublicIPv4AddressAsync().ConfigureAwait(false);
            Info.PublicIpAddress = publicIpResult.Success ? publicIpResult.Value : IPAddress.Loopback;

            _eventLog.Add(new ServerEvent
            {
                EventType = EventType.SendServerInfoStarted,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                LocalFolder = MyTransferFolderPath,
                LocalIpAddress = MyLocalIpAddress,
                LocalPortNumber = MyServerPortNumber,
                PublicIpAddress = MyPublicIpAddress,
                RequestId = request.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            var connectResult = await ConnectToServerAsync().ConfigureAwait(false);
            if (connectResult.Failure)
            {
                return connectResult;
            }

            var socket = connectResult.Value;
            var responseData =
                RequestWrapper.ConstructServerInfoResponse(
                    MyLocalIpAddress.ToString(),
                    MyServerPortNumber,
                    MyPublicIpAddress.ToString(),
                    MyTransferFolderPath);

            var sendMessageDataResult = await SendMessageData(socket, responseData).ConfigureAwait(false);
            if (sendMessageDataResult.Failure)
            {
                return sendMessageDataResult;
            }

            CloseSocket(socket);

            _eventLog.Add(new ServerEvent
            {
                EventType = EventType.SendServerInfoComplete,
                RequestId = request.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            return Result.Ok();
        }

        void ReceiveServerInfo(ServerRequest request)
        {
            var (remoteServerIpAddress,
                remoteServerPort,
                remoteServerPublicIpAddress,
                transferFolder) = RequestUnwrapper.ReadServerInfoResponse(request.Data);

            RemoteServerInfo = new ServerInfo
            {
                PortNumber = remoteServerPort,
                LocalIpAddress = NetworkUtilities.ParseSingleIPv4Address(remoteServerIpAddress).Value,
                PublicIpAddress = NetworkUtilities.ParseSingleIPv4Address(remoteServerPublicIpAddress).Value,
                TransferFolder = transferFolder
            };

            _eventLog.Add(new ServerEvent
            {
                EventType = EventType.ReceivedServerInfo,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                RemoteFolder = RemoteServerTransferFolderPath,
                LocalIpAddress = RemoteServerLocalIpAddress,
                PublicIpAddress = RemoteServerPublicIpAddress,
                RequestId = request.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());
        }
        
        public async Task<Result> ShutdownAsync()
        {
            if (!IsListening)
            {
                return Result.Fail("Server is already shutdown");
            }

            //TODO: This looks awkward, change how shutdown command is sent to local server
            RemoteServerInfo = Info;

            var shutdownResult =
                await SendSimpleMessageToClientAsync(
                    RequestType.ShutdownServerCommand,
                    EventType.SendShutdownServerCommandStarted,
                    EventType.SendShutdownServerCommandComplete).ConfigureAwait(false);

            return shutdownResult.Success
                ? Result.Ok()
                : Result.Fail($"Error occurred shutting down the server + {shutdownResult.Error}");
        }

        void HandleShutdownServerCommand(ServerRequest request)
        {
            (string remoteServerIpAddress,
                int remoteServerPort) = RequestUnwrapper.ReadServerConnectionInfo(request.Data);

            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);

            if (Info.IsEqualTo(RemoteServerInfo))
            {
                ShutdownInitiated = true;
            }

            _eventLog.Add(new ServerEvent
            {
                EventType = EventType.ReceivedShutdownServerCommand,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                RequestId = request.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());
        }

        void ShutdownListenSocket()
        {
            EventOccurred?.Invoke(this,
                new ServerEvent
                { EventType = EventType.ShutdownListenSocketStarted });

            try
            {
                _listenSocket.Shutdown(SocketShutdown.Both);
                _listenSocket.Close();
            }
            catch (SocketException ex)
            {
                _log.Error("Error raised in method Shutdown_listenSocket", ex);
                var errorMessage = $"{ex.Message} ({ex.GetType()} raised in method TplSocketServer.Shutdown_listenSocket)";

                EventOccurred?.Invoke(this,
                    new ServerEvent
                    {
                        EventType = EventType.ShutdownListenSocketCompletedWithError,
                        ErrorMessage = errorMessage
                    });
            }

            EventOccurred?.Invoke(this,
                new ServerEvent
                    {EventType = EventType.ShutdownListenSocketCompletedWithoutError});
        }
    }
}