namespace TplSockets
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    using AaronLuna.Common.IO;
    using AaronLuna.Common.Logging;
    using AaronLuna.Common.Network;
    using AaronLuna.Common.Result;

    public class TplSocketServer
    {
        const string ConfirmationMessage = "handshake";

        const string NotInitializedMessage =
            "Server is unitialized and cannot handle incoming connections";

        bool _serverInitialized;
        bool _transferInitiatedByThisServer;
        int _receivedShutdownCommand;
        int _shutdownComplete;

        IPAddress _ipAddressLastConnection;

        readonly AutoResetEvent _signalSendNextFileChunk = new AutoResetEvent(true);
        readonly Logger _log = new Logger(typeof(TplSocketServer));
        readonly ServerState _state;
        readonly Socket _listenSocket;
        Socket _serverSocket;
        Socket _clientSocket;
        CancellationTokenSource _cts;

        public bool ServerIsRunning
        {
            get => (Interlocked.CompareExchange(ref _shutdownComplete, 1, 1) == 1);
            set
            {
                if (value) Interlocked.CompareExchange(ref _shutdownComplete, 1, 0);
                else Interlocked.CompareExchange(ref _shutdownComplete, 0, 1);
            }
        }

        bool ReceivedShutdownCommand
        {
            get => Interlocked.CompareExchange(ref _receivedShutdownCommand, 1, 1) == 1;
            set
            {
                if (value) Interlocked.CompareExchange(ref _receivedShutdownCommand, 1, 0);
                else Interlocked.CompareExchange(ref _receivedShutdownCommand, 0, 1);
            }
        }
        
        public TplSocketServer()
        {
            ServerIsRunning = false;
            SocketSettings = new SocketSettings
            {
                MaxNumberOfConnections = 5,
                BufferSize = 1024,
                ConnectTimeoutMs = 5000,
                ReceiveTimeoutMs = 5000,
                SendTimeoutMs = 5000
            };

            Info = new ServerInfo()
            {
                TransferFolder = GetDefaultTransferFolder()
            };

            RemoteServerInfo = new ServerInfo();

            _state = new ServerState();
            _listenSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _serverInitialized = false;
            _transferInitiatedByThisServer = true;
        }

        public float TransferUpdateInterval { get; set; }
        public SocketSettings SocketSettings { get; set; }
        public int MaxNumberOfConnections => SocketSettings.MaxNumberOfConnections;
        public int BufferSize => SocketSettings.BufferSize;
        public int ConnectTimeoutMs => SocketSettings.ConnectTimeoutMs;
        public int ReceiveTimeoutMs => SocketSettings.ReceiveTimeoutMs;
        public int SendTimeoutMs => SocketSettings.SendTimeoutMs;

        public ServerInfo Info { get; set; }

        public string MyTransferFolderPath
        {
            get => Info.TransferFolder;
            set => Info.TransferFolder = value;
        }

        public IPAddress MyLocalIpAddress => Info.LocalIpAddress;
        public IPAddress MyPublicIpAddress => Info.PublicIpAddress;
        public int MyServerPort => Info.Port;

        public ServerInfo RemoteServerInfo { get; set; }
        
        public string RemoteServerTransferFolderPath
        {
            get => RemoteServerInfo.TransferFolder;
            set => RemoteServerInfo.TransferFolder = value;
        }

        public IPAddress RemoteServerSessionIpAddress => RemoteServerInfo.SessionIpAddress;
        public IPAddress RemoteServerLocalIpAddress => RemoteServerInfo.LocalIpAddress;
        public IPAddress RemoteServerPublicIpAddress => RemoteServerInfo.PublicIpAddress;
        public int RemoteServerPort => RemoteServerInfo.Port;

        public List<(string filePath, long fileSize)> RemoteServerFileList { get; set; }

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

        public void InitializeServer(IPAddress localIpAddress, int port)
        {
            Info = new ServerInfo(localIpAddress, port);
            _serverInitialized = true;
        }

        public async Task<Result> RunServerAsync()
        {
            if (!_serverInitialized)
            {
                return Result.Fail(NotInitializedMessage);
            }

            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            Logger.Start("server.log");

            var listenResult = Listen(MyServerPort);
            if (listenResult.Failure)
            {
                return listenResult;
            }

            ServerIsRunning = true;
            var runServerResult = await HandleIncomingConnectionsAsync(token).ConfigureAwait(false);

            ServerIsRunning = false;
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
                _listenSocket.Listen(MaxNumberOfConnections);
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
                    LocalPortNumber = MyServerPort
                });

            return Result.Ok();
        }

        async Task<Result> HandleIncomingConnectionsAsync(CancellationToken token)
        {
            // Main loop. Server handles incoming connections until shutdown command is received
            // or an error is encountered
            while (true)
            {
                _transferInitiatedByThisServer = true;

                var acceptResult = await _listenSocket.AcceptTaskAsync(token).ConfigureAwait(false);
                if (acceptResult.Failure)
                {
                    return acceptResult;
                }

                _clientSocket = acceptResult.Value;
                if (!(_clientSocket.RemoteEndPoint is IPEndPoint clientEndPoint))
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

                var clientRequest = await HandleClientRequestAsync(token).ConfigureAwait(false);
                if (token.IsCancellationRequested || ReceivedShutdownCommand)
                {
                    return Result.Ok();
                }

                if (clientRequest.Success) continue;

                EventOccurred?.Invoke(this,
                    new ServerEvent
                    {
                        EventType = EventType.ErrorOccurred,
                        ErrorMessage = clientRequest.Error
                    });
            }
        }

        async Task<Result> HandleClientRequestAsync(CancellationToken token)
        {
            var receiveMessageResult = await ReceiveMessageFromClientAsync().ConfigureAwait(false);
            if (receiveMessageResult.Failure)
            {
                return receiveMessageResult;
            }

            var message = receiveMessageResult.Value;
            var requestResult = await ProcessRequestAsync(message, token).ConfigureAwait(false);

            try
            {
                _clientSocket.Shutdown(SocketShutdown.Both);
                _clientSocket.Close();
            }
            catch (SocketException ex)
            {
                _log.Error("Error raised in method Shutdown_listenSocket", ex);
                return Result.Fail($"{ex.Message} ({ex.GetType()} raised in method TplSocketServer.Shutdown_listenSocket)");
            }

            return requestResult;
        }

        async Task<Result<Message>> ReceiveMessageFromClientAsync()
        {
            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceiveMessageFromClientStarted,
                    RemoteServerIpAddress = _ipAddressLastConnection
                });

            _state.Buffer = new byte[BufferSize];
            _state.UnreadBytes = new List<byte>();

            var messageLengthResult = await DetermineMessageLengthAsync().ConfigureAwait(false);
            if (messageLengthResult.Failure)
            {
                return Result.Fail<Message>(messageLengthResult.Error);
            }

            var messageLength = messageLengthResult.Value;

            var receiveMessageResult = await ReceiveAllMessageBytesAsync(messageLength).ConfigureAwait(false);
            if (receiveMessageResult.Failure)
            {
                return Result.Fail<Message>(receiveMessageResult.Error);
            }

            var messageData = receiveMessageResult.Value;
            var messageTypeData = MessageUnwrapper.ReadInt32(messageData).ToString();
            var messageType = (MessageType)Enum.Parse(typeof(MessageType), messageTypeData);
            var message = new Message
            {
                Data = messageData,
                Type = messageType
            };

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceiveMessageFromClientComplete,
                    RemoteServerIpAddress = _ipAddressLastConnection
                });

            return Result.Ok(message);
        }

        async Task<Result<int>> ReadFromSocketAsync()
        {
            Result<int> receiveResult;
            try
            {
                receiveResult =
                    await _clientSocket.ReceiveWithTimeoutAsync(
                        _state.Buffer,
                        0,
                        BufferSize,
                        SocketFlags.None,
                        ReceiveTimeoutMs)
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

        async Task<Result<int>> DetermineMessageLengthAsync()
        {
            EventOccurred?.Invoke(this,
                new ServerEvent
                    {EventType = EventType.DetermineMessageLengthStarted});

            var readFromSocketResult = await ReadFromSocketAsync().ConfigureAwait(false);
            if (readFromSocketResult.Failure)
            {
                return readFromSocketResult;
            }

            _state.LastBytesReceivedCount = readFromSocketResult.Value;
            var numberOfUnreadBytes = _state.LastBytesReceivedCount - 4;
            var messageLength = MessageUnwrapper.ReadInt32(_state.Buffer);
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

        async Task<Result<byte[]>> ReceiveAllMessageBytesAsync(int messageLength)
        {
            EventOccurred?.Invoke(this,
                new ServerEvent
                    {EventType = EventType.ReceiveMessageBytesStarted});

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
                var readFromSocketResult = await ReadFromSocketAsync().ConfigureAwait(false);
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

        async Task<Result> ProcessRequestAsync(Message message, CancellationToken token)
        {
            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ProcessRequestStarted,
                    MessageType = message.Type
                });

            switch (message.Type)
            {
                case MessageType.TextMessage:
                    ReceiveTextMessage(message.Data);
                    break;

                case MessageType.InboundFileTransferRequest:
                   await InboundFileTransferAsync(message.Data, token).ConfigureAwait(false);
                    break;

                case MessageType.OutboundFileTransferRequest:
                    _transferInitiatedByThisServer = false;
                    await OutboundFileTransferAsync(message.Data).ConfigureAwait(false);
                    break;

                case MessageType.FileTransferAccepted:
                    await HandleFileTransferAcceptedAsync(message.Data, token).ConfigureAwait(false);
                    break;

                case MessageType.FileTransferStalled:
                    HandleStalledFileTransfer(message.Data);
                    break;

                case MessageType.RetryOutboundFileTransfer:
                    await HandleRetryLastFileTransferAsync(message.Data).ConfigureAwait(false);
                    break;

                case MessageType.FileListRequest:
                    await SendFileListAsync(message.Data).ConfigureAwait(false);
                    break;

                case MessageType.FileListResponse:
                    ReceiveFileList(message.Data);
                    break;

                case MessageType.TransferFolderPathRequest:
                    await SendTransferFolderPathAsync(message.Data).ConfigureAwait(false);
                    break;

                case MessageType.TransferFolderPathResponse:
                    ReceiveTransferFolderPath(message.Data);
                    break;

                case MessageType.PublicIpAddressRequest:
                    await SendPublicIpAsync(message.Data).ConfigureAwait(false);
                    break;

                case MessageType.PublicIpAddressResponse:
                    ReceivePublicIpAddress(message.Data);
                    break;

                case MessageType.NoFilesAvailableForDownload:
                    HandleNoFilesAvailableForDownload(message.Data);
                    break;

                case MessageType.FileTransferRejected:
                    HandleFileTransferRejected(message.Data);
                    break;

                //case MessageType.FileTransferCanceled:
                //    return HandleCanceledFileTransfer(message.Data);

                case MessageType.RequestedFolderDoesNotExist:
                    HandleRequestedFolderDoesNotExist(message.Data);
                    break;

                case MessageType.ShutdownServerCommand:
                    HandleShutdownServerCommand(message.Data);
                    break;

                default:
                    var error = $"Unable to determine transfer type, value of '{message.Type}' is invalid.";
                    return Result.Fail(error);
            }

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ProcessRequestComplete,
                    MessageType = message.Type
                });

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
                    RemoteServerPortNumber = RemoteServerPort
                });

            var connectResult = await ConnectToServerAsync().ConfigureAwait(false);
            if (connectResult.Failure)
            {
                return connectResult;
            }

            var messageData =
                MessageWrapper.ConstuctTextMessageRequest(message, MyLocalIpAddress.ToString(), MyServerPort);

            var sendMessageDataResult = await SendMessageData(messageData).ConfigureAwait(false);
            if (sendMessageDataResult.Failure)
            {
                return sendMessageDataResult;
            }

            CloseServerSocket();

            EventOccurred?.Invoke(this,
                new ServerEvent
                    {EventType = EventType.SendTextMessageComplete});

            return Result.Ok();
        }

        void ReceiveTextMessage(byte[] messageData)
        {
            var (message,
                remoteServerIpAddress,
                remoteServerPort) = MessageUnwrapper.ReadTextMessage(messageData);

            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceivedTextMessage,
                    TextMessage = message,
                    RemoteServerIpAddress = RemoteServerSessionIpAddress,
                    RemoteServerPortNumber = RemoteServerPort
                });
        }

        async Task<Result> OutboundFileTransferAsync(byte[] messageData)
        {
            var (requestedFilePath,
                remoteServerIpAddress,
                remoteServerPort,
                remoteFolderPath) = MessageUnwrapper.ReadOutboundFileTransferRequest(messageData);

            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);
            OutgoingFilePath = requestedFilePath;
            RemoteServerTransferFolderPath = remoteFolderPath;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceivedOutboundFileTransferRequest,
                    LocalFolder = Path.GetDirectoryName(OutgoingFilePath),
                    FileName = Path.GetFileName(OutgoingFilePath),
                    FileSizeInBytes = new FileInfo(OutgoingFilePath).Length,
                    RemoteFolder = RemoteServerTransferFolderPath,
                    RemoteServerIpAddress = RemoteServerSessionIpAddress,
                    RemoteServerPortNumber = RemoteServerPort
                });

            if (!File.Exists(requestedFilePath))
            {
                //TODO: Add another event sequence here
                return Result.Fail("File does not exist: " + requestedFilePath);
            }

            return
                await SendFileAsync(
                    RemoteServerSessionIpAddress,
                    RemoteServerPort,
                    OutgoingFilePath,
                    RemoteServerTransferFolderPath).ConfigureAwait(false);
        }

        public async Task<Result> SendFileAsync(
            IPAddress remoteServerIpAddress,
            int remoteServerPort,
            string localFilePath,
            string remoteFolderPath)
        {
            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);
            OutgoingFilePath = localFilePath;
            RemoteServerTransferFolderPath = remoteFolderPath;

            return await SendOutboundFileTransferRequestAsync(
                RemoteServerSessionIpAddress,
                RemoteServerPort,
                OutgoingFilePath,
                RemoteServerTransferFolderPath).ConfigureAwait(false);
        }

        async Task<Result> SendOutboundFileTransferRequestAsync(
            IPAddress remoteServerIpAddress,
            int remoteServerPort,
            string localFilePath,
            string remoteFolderPath)
        {
            if (!File.Exists(localFilePath))
            {
                return Result.Fail("File does not exist: " + localFilePath);
            }
            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);
            OutgoingFilePath = localFilePath;
            RemoteServerTransferFolderPath = remoteFolderPath;

            if (_transferInitiatedByThisServer)
            {
                EventOccurred?.Invoke(this,
                    new ServerEvent
                    {
                        EventType = EventType.RequestOutboundFileTransferStarted,
                        LocalIpAddress = MyLocalIpAddress,
                        LocalPortNumber = MyServerPort,
                        LocalFolder = Path.GetDirectoryName(OutgoingFilePath),
                        FileName = Path.GetFileName(OutgoingFilePath),
                        FileSizeInBytes = _state.OutgoingFileSize,
                        RemoteServerIpAddress = RemoteServerSessionIpAddress,
                        RemoteServerPortNumber = RemoteServerPort,
                        RemoteFolder = RemoteServerTransferFolderPath
                    });
            }

            var connectResult = await ConnectToServerAsync().ConfigureAwait(false);
            if (connectResult.Failure)
            {
                return connectResult;
            }

            var messageData =
                MessageWrapper.ConstructOutboundFileTransferRequest(
                    OutgoingFilePath,
                    _state.OutgoingFileSize,
                    MyLocalIpAddress.ToString(),
                    MyServerPort,
                    RemoteServerTransferFolderPath);

            var sendMessageDataResult = await SendMessageData(messageData).ConfigureAwait(false);
            if (sendMessageDataResult.Failure)
            {
                return sendMessageDataResult;
            }

            EventOccurred?.Invoke(this,
                new ServerEvent
                    {EventType = EventType.RequestOutboundFileTransferComplete});

            return Result.Ok();
        }

        void HandleFileTransferRejected(byte[] messageData)
        {
            (string remoteServerIpAddress,
                int remoteServerPort) = MessageUnwrapper.ReadServerConnectionInfo(messageData);

            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);

            //TODO: Investigate why this causes a bug that fails my unit tests
            //OutgoingFilePath = string.Empty;
            //RemoteServerTransferFolderPath = string.Empty;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ClientRejectedFileTransfer,
                    RemoteServerIpAddress = RemoteServerSessionIpAddress,
                    RemoteServerPortNumber = RemoteServerPort
                });
        }

        async Task<Result> HandleFileTransferAcceptedAsync(byte[] messageData, CancellationToken token)
        {
            (string remoteServerIpAddress,
                int remoteServerPort) = MessageUnwrapper.ReadServerConnectionInfo(messageData);

            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ClientAcceptedFileTransfer,
                    RemoteServerIpAddress = RemoteServerSessionIpAddress,
                    RemoteServerPortNumber = RemoteServerPort
                });

            return await SendFileBytesAsync(OutgoingFilePath, token).ConfigureAwait(false);
        }

        async Task<Result> SendFileBytesAsync(string localFilePath, CancellationToken token)
        {
            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.SendFileBytesStarted,
                    RemoteServerIpAddress = RemoteServerSessionIpAddress,
                    RemoteServerPortNumber = RemoteServerPort
                });

            var bytesRemaining = _state.OutgoingFileSize;
            var fileChunkSentCount = 0;
            _state.FileTransferCanceled = false;

            using (var file = File.OpenRead(localFilePath))
            {
                while (bytesRemaining > 0)
                {
                    if (token.IsCancellationRequested)
                    {
                        return Result.Ok();
                    }

                    var fileChunkSize = (int) Math.Min(BufferSize, bytesRemaining);
                    _state.Buffer = new byte[fileChunkSize];

                    var numberOfBytesToSend = file.Read(_state.Buffer, 0, fileChunkSize);
                    bytesRemaining -= numberOfBytesToSend;

                    var offset = 0;
                    var socketSendCount = 0;
                    while (numberOfBytesToSend > 0)
                    {
                        var sendFileChunkResult =
                            await _serverSocket.SendWithTimeoutAsync(
                                _state.Buffer,
                                offset,
                                fileChunkSize,
                                SocketFlags.None,
                                SendTimeoutMs).ConfigureAwait(false);

                        if (_state.FileTransferCanceled)
                        {
                            const string fileTransferStalledErrorMessage =
                                "Aborting file transfer, client says that data is no longer being received";

                            return Result.Fail(fileTransferStalledErrorMessage);
                        }

                        if (sendFileChunkResult.Failure)
                        {
                            return sendFileChunkResult;
                        }

                        _state.LastBytesSentCount = sendFileChunkResult.Value;
                        numberOfBytesToSend -= _state.LastBytesSentCount;
                        offset += _state.LastBytesSentCount;
                        socketSendCount++;
                    }

                    fileChunkSentCount++;

                    if (_state.OutgoingFileSize > (10 * BufferSize)) continue;
                    SocketEventOccurred?.Invoke(this,
                        new ServerEvent
                        {
                            EventType = EventType.SentFileChunkToClient,
                            FileSizeInBytes = _state.OutgoingFileSize,
                            CurrentFileBytesSent = fileChunkSize,
                            FileBytesRemaining = bytesRemaining,
                            FileChunkSentCount = fileChunkSentCount,
                            SocketSendCount = socketSendCount
                        });
                }

                EventOccurred?.Invoke(this,
                    new ServerEvent
                    {
                        EventType = EventType.SendFileBytesComplete,
                        RemoteServerIpAddress = RemoteServerSessionIpAddress,
                        RemoteServerPortNumber = RemoteServerPort
                    });

                var receiveConfirationMessageResult =
                    await ReceiveConfirmationFileTransferCompleteAsync().ConfigureAwait(false);

                if (receiveConfirationMessageResult.Failure)
                {
                    return receiveConfirationMessageResult;
                }

                _serverSocket.Shutdown(SocketShutdown.Both);
                _serverSocket.Close();

                return Result.Ok();
            }
        }

        async Task<Result> ReceiveConfirmationFileTransferCompleteAsync()
        {
            var buffer = new byte[BufferSize];
            Result<int> receiveMessageResult;
            int bytesReceived;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceiveConfirmationMessageStarted,
                    RemoteServerIpAddress = RemoteServerSessionIpAddress,
                    RemoteServerPortNumber = RemoteServerPort
                });

            try
            {
                receiveMessageResult =
                    await _serverSocket.ReceiveAsync(
                        buffer,
                        0,
                        BufferSize,
                        0).ConfigureAwait(false);

                bytesReceived = receiveMessageResult.Value;
            }
            catch (SocketException ex)
            {
                _log.Error("Error raised in method ReceiveConfirmationFileTransferCompleteAsync", ex);
                return Result.Fail(
                    $"{ex.Message} ({ex.GetType()} raised in method TplSocketServer.ReceiveConfirmationFileTransferCompleteAsync)");
            }
            catch (TimeoutException ex)
            {
                _log.Error("Error raised in method ReceiveConfirmationFileTransferCompleteAsync", ex);
                return Result.Fail(
                    $"{ex.Message} ({ex.GetType()} raised in method TplSocketServer.ReceiveConfirmationFileTransferCompleteAsync)");
            }

            if (receiveMessageResult.Failure || bytesReceived == 0)
            {
                return Result.Fail("Error receiving confirmation message from remote server");
            }

            var confirmationMessage = Encoding.ASCII.GetString(buffer, 0, bytesReceived);

            if (confirmationMessage != ConfirmationMessage)
            {
                return Result.Fail(
                    $"Confirmation message doesn't match:\n\tExpected:\t{ConfirmationMessage}\n\tActual:\t{confirmationMessage}");
            }

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceiveConfirmationMessageComplete,
                    RemoteServerIpAddress = RemoteServerSessionIpAddress,
                    RemoteServerPortNumber = RemoteServerPort,
                    ConfirmationMessage = confirmationMessage
                });

            return Result.Ok();
        }

        public async Task<Result> GetFileAsync(
            string remoteServerIpAddress,
            int remoteServerPort,
            string remoteFilePath,
            string localFolderPath)
        {
            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);
            RemoteFilePath = remoteFilePath;
            RemoteServerTransferFolderPath = Path.GetDirectoryName(remoteFilePath);
            MyTransferFolderPath = localFolderPath;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.RequestInboundFileTransferStarted,
                    RemoteServerIpAddress = RemoteServerSessionIpAddress,
                    RemoteServerPortNumber = RemoteServerPort,
                    RemoteFolder = RemoteServerTransferFolderPath,
                    FileName = Path.GetFileName(RemoteFilePath),
                    LocalIpAddress = MyLocalIpAddress,
                    LocalPortNumber = MyServerPort,
                    LocalFolder = MyTransferFolderPath,
                });

            var connectResult = await ConnectToServerAsync().ConfigureAwait(false);
            if (connectResult.Failure)
            {
                return connectResult;
            }

            var messageData =
                MessageWrapper.ConstructInboundFileTransferRequest(
                    RemoteFilePath,
                    MyLocalIpAddress.ToString(),
                    MyServerPort,
                    MyTransferFolderPath);

            var sendMessageDataResult = await SendMessageData(messageData).ConfigureAwait(false);
            if (sendMessageDataResult.Failure)
            {
                return sendMessageDataResult;
            }

            CloseServerSocket();

            EventOccurred?.Invoke(this,
                new ServerEvent
                    {EventType = EventType.RequestInboundFileTransferComplete});

            return Result.Ok();
        }

        async Task<Result> InboundFileTransferAsync(byte[] messageData, CancellationToken token)
        {
            var (localFilePath,
                fileSizeBytes,
                remoteServerIpAddress,
                remoteServerPort) = MessageUnwrapper.ReadInboundFileTransferRequest(messageData);

            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);
            IncomingFilePath = localFilePath;
            _state.IncomingFileSize = fileSizeBytes;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceivedInboundFileTransferRequest,
                    LocalFolder = Path.GetDirectoryName(IncomingFilePath),
                    FileName = Path.GetFileName(IncomingFilePath),
                    FileSizeInBytes = _state.IncomingFileSize,
                    RemoteServerIpAddress = RemoteServerSessionIpAddress,
                    RemoteServerPortNumber = RemoteServerPort
                });

            if (File.Exists(IncomingFilePath))
            {
                return
                    await SendSimpleMessageToClientAsync(
                        MessageType.FileTransferRejected,
                        EventType.SendFileTransferRejectedStarted,
                        EventType.SendFileTransferRejectedComplete).ConfigureAwait(false);
            }

            var acceptTransferResult =
                await SendSimpleMessageToClientAsync(
                    MessageType.FileTransferAccepted,
                    EventType.SendFileTransferAcceptedStarted,
                    EventType.SendFileTransferAcceptedComplete).ConfigureAwait(false);

            if (acceptTransferResult.Failure)
            {
                return acceptTransferResult;
            }

            var receiveFileResult = await ReceiveFileAsync(token).ConfigureAwait(false);
            if (receiveFileResult.Failure)
            {
                return receiveFileResult;
            }

            var sendConfirmationMessageResult =
                await ConfirmFileTransferComplete().ConfigureAwait(false);

            return sendConfirmationMessageResult.Success
                ? Result.Ok()
                : sendConfirmationMessageResult;
        }

        async Task<Result> ReceiveFileAsync(CancellationToken token)
        {
            var startTime = DateTime.Now;
            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceiveFileBytesStarted,
                    RemoteServerIpAddress = RemoteServerSessionIpAddress,
                    RemoteServerPortNumber = RemoteServerPort,
                    FileTransferStartTime = startTime,
                    FileSizeInBytes = _state.IncomingFileSize
                });

            var receiveCount = 0;
            long totalBytesReceived = 0;
            var bytesRemaining = _state.IncomingFileSize;
            float percentComplete = 0;
            _state.FileTransferStalled = false;

            if (_state.UnreadBytes.Count > 0)
            {
                totalBytesReceived += _state.UnreadBytes.Count;
                bytesRemaining -= _state.UnreadBytes.Count;

                var writeBytesResult =
                    FileHelper.WriteBytesToFile(
                        IncomingFilePath,
                        _state.UnreadBytes.ToArray(),
                        _state.UnreadBytes.Count);

                if (writeBytesResult.Failure)
                {
                    return writeBytesResult;
                }

                EventOccurred?.Invoke(this,
                    new ServerEvent
                    {
                        EventType = EventType.CopySavedBytesToIncomingFile,
                        CurrentFileBytesReceived = _state.UnreadBytes.Count,
                        TotalFileBytesReceived = totalBytesReceived,
                        FileSizeInBytes = _state.IncomingFileSize,
                        FileBytesRemaining = bytesRemaining
                    });
            }

            // Read file bytes from transfer socket until 
            //      1. the entire file has been received OR 
            //      2. Data is no longer being received OR
            //      3, Transfer is canceled
            while (bytesRemaining > 0)
            {
                if (token.IsCancellationRequested)
                {
                    return Result.Ok();
                }

                var readFromSocketResult = await ReadFromSocketAsync().ConfigureAwait(false);
                if (readFromSocketResult.Failure)
                {
                    return Result.Fail(readFromSocketResult.Error);
                }

                _state.LastBytesReceivedCount = readFromSocketResult.Value;
                var receivedBytes = new byte[_state.LastBytesReceivedCount];

                if (_state.LastBytesReceivedCount == 0)
                {
                    return Result.Fail("Socket is no longer receiving data, must abort file transfer");
                }

                var writeBytesResult =
                    FileHelper.WriteBytesToFile(
                        IncomingFilePath,
                        receivedBytes,
                        _state.LastBytesReceivedCount);

                if (writeBytesResult.Failure)
                {
                    return writeBytesResult;
                }

                receiveCount++;
                totalBytesReceived += _state.LastBytesReceivedCount;
                bytesRemaining -= _state.LastBytesReceivedCount;
                var checkPercentComplete = totalBytesReceived / (float) _state.IncomingFileSize;
                var changeSinceLastUpdate = checkPercentComplete - percentComplete;

                // this method fires on every socket read event, which could be hurdreds of thousands
                // of times or millions of times depending on the file size and buffer size. Since this 
                // event is only used by myself when debugging small test files, I limited this
                // event to only fire when the size of the file will result in less than 10 read events
                if (_state.IncomingFileSize < (10 * BufferSize))
                {
                    SocketEventOccurred?.Invoke(this,
                        new ServerEvent
                        {
                            EventType = EventType.ReceivedFileBytesFromSocket,
                            SocketReadCount = receiveCount,
                            BytesReceived = _state.LastBytesReceivedCount,
                            CurrentFileBytesReceived = _state.LastBytesReceivedCount,
                            TotalFileBytesReceived = totalBytesReceived,
                            FileSizeInBytes = _state.IncomingFileSize,
                            FileBytesRemaining = bytesRemaining,
                            PercentComplete = percentComplete
                        });
                }

                // Report progress in intervals which are set by the user in the settings file
                if (changeSinceLastUpdate > TransferUpdateInterval)
                {
                    percentComplete = checkPercentComplete;
                    FileTransferProgress?.Invoke(this,
                        new ServerEvent
                        {
                            EventType = EventType.UpdateFileTransferProgress,
                            TotalFileBytesReceived = totalBytesReceived,
                            FileSizeInBytes = _state.IncomingFileSize,
                            FileBytesRemaining = bytesRemaining,
                            PercentComplete = percentComplete
                        });
                }
            }

            if (_state.FileTransferStalled)
            {
                return Result.Fail(
                    "Data is no longer bring received from remote client, file transfer has been canceled");
            }

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceiveFileBytesComplete,
                    FileTransferStartTime = startTime,
                    FileTransferCompleteTime = DateTime.Now,
                    FileSizeInBytes = _state.IncomingFileSize,
                    RemoteServerIpAddress = RemoteServerSessionIpAddress,
                    RemoteServerPortNumber = RemoteServerPort
                });

            return Result.Ok();
        }

        public async Task<Result> SendNotificationFileTransferStalledAsync()
        {
            _state.FileTransferStalled = true;

            return
                await SendSimpleMessageToClientAsync(
                    MessageType.FileTransferStalled,
                    EventType.SendFileTransferStalledStarted,
                    EventType.SendFileTransferStalledComplete).ConfigureAwait(false);
        }

        void HandleStalledFileTransfer(byte[] messageData)
        {
            (string remoteServerIpAddress,
                int remoteServerPort) = MessageUnwrapper.ReadServerConnectionInfo(messageData);

            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);

            EventOccurred?.Invoke(this,
            new ServerEvent
            {
                EventType = EventType.FileTransferStalled,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPort
            });

            _state.FileTransferCanceled = true;
            _signalSendNextFileChunk.Set();
        }

        public async Task<Result> RetryLastFileTransferAsync(
            IPAddress remoteServerIpAddress,
            int remoteServerPort)
        {
            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);

            return await SendSimpleMessageToClientAsync(
                MessageType.RetryOutboundFileTransfer,
                EventType.RetryOutboundFileTransferStarted,
                EventType.RetryOutboundFileTransferComplete).ConfigureAwait(false);
        }

        async Task<Result> HandleRetryLastFileTransferAsync(byte[] messageData)
        {
            (string remoteServerIpAddress,
                int remoteServerPort) = MessageUnwrapper.ReadServerConnectionInfo(messageData);

            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);

            EventOccurred?.Invoke(this,
            new ServerEvent
            {
                EventType = EventType.ReceivedRetryOutboundFileTransferRequest,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPort
            });

            return
                await SendFileAsync(
                    RemoteServerSessionIpAddress,
                    RemoteServerPort,
                    OutgoingFilePath,
                    RemoteServerTransferFolderPath).ConfigureAwait(false);
        }

        async Task<Result> ConfirmFileTransferComplete()
        {
            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.SendConfirmationMessageStarted,
                    RemoteServerIpAddress = RemoteServerSessionIpAddress,
                    RemoteServerPortNumber = RemoteServerPort,
                    ConfirmationMessage = ConfirmationMessage
                });

            var confirmationMessageData = Encoding.ASCII.GetBytes(ConfirmationMessage);

            var sendConfirmatinMessageResult =
                await _clientSocket.SendWithTimeoutAsync(
                    confirmationMessageData,
                    0,
                    confirmationMessageData.Length,
                    SocketFlags.None,
                    SendTimeoutMs).ConfigureAwait(false);

            if (sendConfirmatinMessageResult.Failure)
            {
                return sendConfirmatinMessageResult;
            }

            EventOccurred?.Invoke(this,
                new ServerEvent
                    {EventType = EventType.SendConfirmationMessageComplete});

            return Result.Ok();
        }

        async Task<Result> SendSimpleMessageToClientAsync(
            MessageType messageType,
            EventType sendMessageStartedEventType,
            EventType sendMessageCompleteEventType)
        {
            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = sendMessageStartedEventType,
                    RemoteServerIpAddress = RemoteServerSessionIpAddress,
                    RemoteServerPortNumber = RemoteServerPort,
                    LocalIpAddress = MyLocalIpAddress,
                    LocalPortNumber = MyServerPort
                });

            var connectResult = await ConnectToServerAsync().ConfigureAwait(false);
            if (connectResult.Failure)
            {
                return connectResult;
            }

            var messageData =
                MessageWrapper.ConstructGenericMessage(
                    messageType,
                    MyLocalIpAddress.ToString(),
                    MyServerPort);

            var sendMessageDataResult = await SendMessageData(messageData).ConfigureAwait(false);
            if (sendMessageDataResult.Failure)
            {
                return sendMessageDataResult;
            }

            CloseServerSocket();

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = sendMessageCompleteEventType,
                    RemoteServerIpAddress = RemoteServerSessionIpAddress,
                    RemoteServerPortNumber = RemoteServerPort,
                    LocalIpAddress = MyLocalIpAddress,
                    LocalPortNumber = MyServerPort
                });

            return Result.Ok();
        }

        async Task<Result> ConnectToServerAsync()
        {
            _serverSocket =
                new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ConnectToRemoteServerStarted,
                    RemoteServerIpAddress = RemoteServerSessionIpAddress,
                    RemoteServerPortNumber = RemoteServerPort
                });

            var connectResult =
                await _serverSocket.ConnectWithTimeoutAsync(
                    RemoteServerSessionIpAddress,
                    RemoteServerPort,
                    ConnectTimeoutMs).ConfigureAwait(false);

            if (connectResult.Failure)
            {
                return connectResult;
            }

            EventOccurred?.Invoke(this,
                new ServerEvent
                    {EventType = EventType.ConnectToRemoteServerComplete});

            return Result.Ok();
        }

        async Task<Result> SendMessageData(byte[] messageData)
        {
            var messageLength = BitConverter.GetBytes(messageData.Length);

            var sendMessageLengthResult =
                await _serverSocket.SendWithTimeoutAsync(
                    messageLength,
                    0,
                    messageLength.Length,
                    SocketFlags.None,
                    SendTimeoutMs).ConfigureAwait(false);

            if (sendMessageLengthResult.Failure)
            {
                return sendMessageLengthResult;
            }

            var sendMessageResult =
                await _serverSocket.SendWithTimeoutAsync(
                    messageData,
                    0,
                    messageData.Length,
                    SocketFlags.None,
                    SendTimeoutMs).ConfigureAwait(false);

            return sendMessageResult.Success
                ? Result.Ok()
                : sendMessageResult;
        }

        void CloseServerSocket()
        {
            _serverSocket.Shutdown(SocketShutdown.Both);
            _serverSocket.Close();
        }

        public async Task<Result> RequestFileListAsync(
            string remoteServerIpAddress,
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
                    LocalPortNumber = MyServerPort,
                    RemoteServerIpAddress = RemoteServerSessionIpAddress,
                    RemoteServerPortNumber = RemoteServerPort,
                    RemoteFolder = RemoteServerTransferFolderPath
                });

            var connectResult = await ConnectToServerAsync().ConfigureAwait(false);
            if (connectResult.Failure)
            {
                return connectResult;
            }

            var messageData =
                MessageWrapper.ConstructFileListRequest(
                    MyLocalIpAddress.ToString(),
                    MyServerPort,
                    RemoteServerTransferFolderPath);

            var sendMessageDataResult = await SendMessageData(messageData).ConfigureAwait(false);
            if (sendMessageDataResult.Failure)
            {
                return sendMessageDataResult;
            }

            CloseServerSocket();

            EventOccurred?.Invoke(this,
            new ServerEvent {EventType = EventType.RequestFileListComplete});

            return Result.Ok();
        }

        async Task<Result> SendFileListAsync(byte[] messageData)
        {
            (string remoteServerIpAddress,
                int remoteServerPort,
                string targetFolderPath) = MessageUnwrapper.ReadFileListRequest(messageData);

            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);
            MyTransferFolderPath = targetFolderPath;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceivedFileListRequest,
                    RemoteServerIpAddress = RemoteServerSessionIpAddress,
                    RemoteServerPortNumber = RemoteServerPort,
                    RemoteFolder = MyTransferFolderPath
                });

            if (!Directory.Exists(MyTransferFolderPath))
            {
                return
                    await SendSimpleMessageToClientAsync(
                        MessageType.RequestedFolderDoesNotExist,
                        EventType.SendNotificationFolderDoesNotExistStarted,
                        EventType.SendNotificationFolderDoesNotExistComplete).ConfigureAwait(false);
            }

            List<string> listOfFiles;
            try
            {
                listOfFiles =
                    Directory.GetFiles(MyTransferFolderPath).ToList()
                        .Select(f => new FileInfo(f)).Where(fi => !fi.Name.StartsWith('.'))
                        .Select(fi => fi.ToString()).ToList();
            }
            catch (IOException ex)
            {
                _log.Error("Error raised in method SendFileListAsync", ex);
                return Result.Fail($"{ex.Message} ({ex.GetType()} raised in method TplSocketServer.SendFileListAsync)");
            }

            if (listOfFiles.Count == 0)
            {
                return await SendSimpleMessageToClientAsync(
                    MessageType.NoFilesAvailableForDownload,
                    EventType.SendNotificationNoFilesToDownloadStarted,
                    EventType.SendNotificationNoFilesToDownloadComplete).ConfigureAwait(false);
            }

            var fileInfoList = new List<(string, long)>();
            foreach (var file in listOfFiles)
            {
                var fileSize = new FileInfo(file).Length;
                fileInfoList.Add((file, fileSize));
            }

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.SendFileListStarted,
                    LocalIpAddress = MyLocalIpAddress,
                    LocalPortNumber = MyServerPort,
                    RemoteServerIpAddress = RemoteServerSessionIpAddress,
                    RemoteServerPortNumber = RemoteServerPort,
                    RemoteServerFileList = fileInfoList,
                    LocalFolder = MyTransferFolderPath,
                });

            var connectResult = await ConnectToServerAsync().ConfigureAwait(false);
            if (connectResult.Failure)
            {
                return connectResult;
            }

            var responseData =
                MessageWrapper.ConstructFileListResponse(
                    fileInfoList,
                    "*",
                    "|",
                    MyLocalIpAddress.ToString(),
                    MyServerPort,
                    MyTransferFolderPath);

            var sendMessageDataResult = await SendMessageData(responseData).ConfigureAwait(false);
            if (sendMessageDataResult.Failure)
            {
                return sendMessageDataResult;
            }

            CloseServerSocket();

            EventOccurred?.Invoke(this,
                new ServerEvent
                    {EventType = EventType.SendFileListComplete});

            return Result.Ok();
        }

        void HandleRequestedFolderDoesNotExist(byte[] messageData)
        {
            (string remoteServerIpAddress,
                int remoteServerPort) = MessageUnwrapper.ReadServerConnectionInfo(messageData);

            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceivedNotificationFolderDoesNotExist,
                    RemoteServerIpAddress = RemoteServerSessionIpAddress,
                    RemoteServerPortNumber = RemoteServerPort
                });
        }

        void HandleNoFilesAvailableForDownload(byte[] messageData)
        {
            (string remoteServerIpAddress,
                int remoteServerPort) = MessageUnwrapper.ReadServerConnectionInfo(messageData);

            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceivedNotificationNoFilesToDownload,
                    RemoteServerIpAddress = RemoteServerSessionIpAddress,
                    RemoteServerPortNumber = RemoteServerPort
                });
        }

        void ReceiveFileList(byte[] messageData)
        {
            var (remoteServerIpAddress,
                remoteServerPort,
                transferFolder,
                fileList) = MessageUnwrapper.ReadFileListResponse(messageData);

            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);
            RemoteServerTransferFolderPath = transferFolder;

            RemoteServerFileList = fileList;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceivedFileList,
                    LocalIpAddress = MyLocalIpAddress,
                    LocalPortNumber = MyServerPort,
                    RemoteServerIpAddress = RemoteServerSessionIpAddress,
                    RemoteServerPortNumber = RemoteServerPort,
                    RemoteFolder = RemoteServerTransferFolderPath,
                    RemoteServerFileList = RemoteServerFileList
                });
        }

        public async Task<Result> RequestTransferFolderPathAsync(
            string remoteServerIpAddress,
            int remoteServerPort)
        {
            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);

            return
                await SendSimpleMessageToClientAsync(
                    MessageType.TransferFolderPathRequest,
                    EventType.RequestTransferFolderPathStarted,
                    EventType.RequestTransferFolderPathComplete).ConfigureAwait(false);
        }

        async Task<Result> SendTransferFolderPathAsync(byte[] messageData)
        {
            (string remoteServerIpAddress,
                int remoteServerPort) = MessageUnwrapper.ReadServerConnectionInfo(messageData);

            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceivedTransferFolderPathRequest,
                    RemoteServerIpAddress = RemoteServerSessionIpAddress,
                    RemoteServerPortNumber = RemoteServerPort
                });

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.SendTransferFolderPathStarted,
                    RemoteServerIpAddress = RemoteServerSessionIpAddress,
                    RemoteServerPortNumber = RemoteServerPort,
                    LocalFolder = MyTransferFolderPath
                });

            var connectResult = await ConnectToServerAsync().ConfigureAwait(false);
            if (connectResult.Failure)
            {
                return connectResult;
            }

            var responseData =
                MessageWrapper.ConstructTransferFolderResponse(
                    MyLocalIpAddress.ToString(),
                    MyServerPort,
                    MyTransferFolderPath);

            var sendMessageDataResult = await SendMessageData(responseData).ConfigureAwait(false);
            if (sendMessageDataResult.Failure)
            {
                return sendMessageDataResult;
            }

            CloseServerSocket();

            EventOccurred?.Invoke(this,
                new ServerEvent
                    {EventType = EventType.SendTransferFolderPathComplete });

            return Result.Ok();
        }

        void ReceiveTransferFolderPath(byte[] messageData)
        {
            var (remoteServerIpAddress,
                remoteServerPort,
                transferFolder) = MessageUnwrapper.ReadTransferFolderResponse(messageData);

            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);
            RemoteServerTransferFolderPath = transferFolder;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceivedTransferFolderPath,
                    RemoteServerIpAddress = RemoteServerSessionIpAddress,
                    RemoteServerPortNumber = RemoteServerPort,
                    RemoteFolder = RemoteServerTransferFolderPath
                });
        }

        public async Task<Result> RequestPublicIpAsync(
            string remoteServerIpAddress,
            int remoteServerPort)
        {
            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);

            return
                await SendSimpleMessageToClientAsync(
                    MessageType.PublicIpAddressRequest,
                    EventType.RequestPublicIpAddressStarted,
                    EventType.RequestPublicIpAddressComplete).ConfigureAwait(false);
        }

        async Task<Result> SendPublicIpAsync(byte[] messageData)
        {
            (string remoteServerIpAddress,
                int remoteServerPort) = MessageUnwrapper.ReadServerConnectionInfo(messageData);

            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceivedPublicIpAddressRequest,
                    RemoteServerIpAddress = RemoteServerSessionIpAddress,
                    RemoteServerPortNumber = RemoteServerPort
                });

            var publicIpResult = await NetworkUtilities.GetPublicIPv4AddressAsync().ConfigureAwait(false);
            if (publicIpResult.Failure)
            {
                return Result.Fail(publicIpResult.Error);
            }

            Info.PublicIpAddress = publicIpResult.Value;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.SendPublicIpAddressStarted,
                    RemoteServerIpAddress = RemoteServerSessionIpAddress,
                    RemoteServerPortNumber = RemoteServerPort,
                    LocalIpAddress = MyLocalIpAddress,
                    LocalPortNumber = MyServerPort,
                    PublicIpAddress = MyPublicIpAddress
                });

            var connectResult = await ConnectToServerAsync().ConfigureAwait(false);
            if (connectResult.Failure)
            {
                return connectResult;
            }

            var responseData =
                MessageWrapper.ConstructPublicIpAddressResponse(
                    MyLocalIpAddress.ToString(),
                    MyServerPort,
                    MyPublicIpAddress.ToString());

            var sendMessageDataResult = await SendMessageData(responseData).ConfigureAwait(false);
            if (sendMessageDataResult.Failure)
            {
                return sendMessageDataResult;
            }

            CloseServerSocket();

            EventOccurred?.Invoke(this,
                new ServerEvent
                    {EventType = EventType.SendPublicIpAddressComplete});

            return Result.Ok();
        }

        void ReceivePublicIpAddress(byte[] messageData)
        {
            var (remoteServerIpAddress,
                remoteServerPort,
                publicIpAddress) = MessageUnwrapper.ReadPublicIpAddressResponse(messageData);

            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort)
            {
                PublicIpAddress = NetworkUtilities.ParseSingleIPv4Address(publicIpAddress).Value
            };

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceivedPublicIpAddress,
                    RemoteServerIpAddress = RemoteServerSessionIpAddress,
                    RemoteServerPortNumber = RemoteServerPort,
                    PublicIpAddress = RemoteServerPublicIpAddress
                });
        }

        public async Task<Result> ShutdownServerAsync()
        {
            if (!ServerIsRunning)
            {
                return Result.Fail("Server is already shutdown");
            }

            //TODO: This looks awkward, change how shutdown command is sent to local server
            RemoteServerInfo= Info;

            var shutdownResult =
                await SendSimpleMessageToClientAsync(
                    MessageType.ShutdownServerCommand,
                    EventType.SendShutdownServerCommandStarted,
                    EventType.SendShutdownServerCommandComplete).ConfigureAwait(false);

            return shutdownResult.Success
                ? Result.Ok()
                : Result.Fail($"Error occurred shutting down the server + {shutdownResult.Error}");
        }

        void HandleShutdownServerCommand(byte[] messageData)
        {
            (string remoteServerIpAddress,
                int remoteServerPort) = MessageUnwrapper.ReadServerConnectionInfo(messageData);

            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);

            if (Info.IsEqualTo(RemoteServerInfo))
            {
                ReceivedShutdownCommand = true;
            }

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceivedShutdownServerCommand,
                    RemoteServerIpAddress = RemoteServerSessionIpAddress,
                    RemoteServerPortNumber = RemoteServerPort
                });
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