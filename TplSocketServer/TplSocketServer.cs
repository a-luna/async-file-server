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

    using AaronLuna.Common.Enums;
    using AaronLuna.Common.IO;
    using AaronLuna.Common.Logging;
    using AaronLuna.Common.Network;
    using AaronLuna.Common.Result;

    public class TplSocketServer
    {
        const string ConfirmationMessage = "handshake";

        int _receivedShutdownCommand;
        int _shutdownComplete;

        readonly AutoResetEvent _signalSendNextFileChunk = new AutoResetEvent(true);
        readonly Logger _log = new Logger(typeof(TplSocketServer));
        readonly ServerState _state;
        readonly Socket _listenSocket;
        Socket _serverSocket;
        Socket _clientSocket;
        CancellationTokenSource _cts;

        public TplSocketServer(IPAddress localIpAddress, int port)
        {
            _state = new ServerState(localIpAddress, port);

            _listenSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _serverSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        }

        public float TransferUpdateInterval
        {
            get => _state.TransferUpdateInterval;
            set => _state.TransferUpdateInterval = value;
        }

        public SocketSettings SocketSettings
        {
            get => _state.SocketSettings;
            set => _state.SocketSettings = value;
        }

        public ConnectionInfo MyInfo => _state.MyInfo;

        public ConnectionInfo ClientInfo
        {
            get => _state.ClientInfo;
            set => _state.ClientInfo = value;
        }

        public string MyTransferFolderPath
        {
            get => _state.MyTransferFolderPath;
            set => _state.MyTransferFolderPath = value;
        }

        public string ClientTransferFolderPath
        {
            get => _state.ClientTransferFolderPath;
            set => _state.ClientTransferFolderPath = value;
        }
        
        public IPAddress MyLocalIpAddress => _state.MyLocalIpAddress;
        public IPAddress MyPublicIpAddress => _state.MyPublicIpAddress;
        public int MyServerPort => _state.MyServerPort;

        public IPAddress ClientSessionIpAddress => _state.ClientSessionIpAddress;
        public IPAddress ClientLocalIpAddress => _state.ClientLocalIpAddress;
        public IPAddress ClientPublicIpAddress => _state.ClientPublicIpAddress;
        public int ClientServerPort => _state.ClientServerPort;

        public string IncomingFilePath => _state.IncomingFilePath;
        public List<(string filePath, long fileSize)> FileListInfo => _state.FileListInfo;

        public event EventHandler<ServerEvent> EventOccurred;
        public event EventHandler<ServerEvent> SocketEventOccurred;
        public event EventHandler<ServerEvent> FileTransferProgress;

        bool ReceivedShutdownCommand
        {
            get => Interlocked.CompareExchange(ref _receivedShutdownCommand, 1, 1) == 1;
            set
            {
                if (value) Interlocked.CompareExchange(ref _receivedShutdownCommand, 1, 0);
                else Interlocked.CompareExchange(ref _receivedShutdownCommand, 0, 1);
            }
        }

        bool ShutdownComplete
        {
            get => (Interlocked.CompareExchange(ref _shutdownComplete, 1, 1) == 1);
            set
            {
                if (value) Interlocked.CompareExchange(ref _shutdownComplete, 1, 0);
                else Interlocked.CompareExchange(ref _shutdownComplete, 0, 1);
            }
        }

        public async Task<Result> HandleIncomingConnectionsAsync()
        {
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            Logger.Start("server.log");

            var listenResult = Listen(string.Empty, MyServerPort);
            if (listenResult.Failure)
            {
                return listenResult;
            }

            var runServerResult = await WaitForConnectionsAsync(token);

            EventOccurred?.Invoke(this,
                new ServerEvent
                    {EventType = EventType.ExitMainLoop});

            if (ReceivedShutdownCommand)
            {
                ShutdownComplete = true;
            }

            return runServerResult;
        }

        Result Listen(string localIpAdress, int localPort)
        {
            IPAddress ipToBind;
            if (string.IsNullOrEmpty(localIpAdress))
            {
                ipToBind = IPAddress.Any;
            }
            else
            {
                var parsedIp = Network.ParseSingleIPv4Address(localIpAdress);
                if (parsedIp.Failure)
                {
                    return parsedIp;

                }

                ipToBind = parsedIp.Value;
            }

            var ipEndPoint = new IPEndPoint(ipToBind, localPort);
            try
            {
                _listenSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _listenSocket.Bind(ipEndPoint);
                _listenSocket.Listen(SocketSettings.MaxNumberOfConnections);
            }
            catch (SocketException ex)
            {
                _log.Error("Error raised in method Listen", ex);
                return Result.Fail($"{ex.Message} ({ex.GetType()} raised in method TplSocketServer.Listen)");
            }

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ServerIsListening,
                    LocalPortNumber = MyServerPort
                });

            return Result.Ok();
        }

        async Task<Result> WaitForConnectionsAsync(CancellationToken token)
        {
            EventOccurred?.Invoke(this,
                new ServerEvent
                    {EventType = EventType.EnterMainLoop});

            // Main loop. Server handles incoming connections until encountering an error
            while (true)
            {
                var acceptResult = await _listenSocket.AcceptTaskAsync(token).ConfigureAwait(false);

                if (acceptResult.Failure)
                {
                    return acceptResult;
                }

                if (token.IsCancellationRequested)
                {
                    return Result.Ok();
                }

                _clientSocket = acceptResult.Value;

                if (!(_clientSocket.RemoteEndPoint is IPEndPoint clientEndPoint))
                {
                    return Result.Fail("Error occurred casting _state._clientSocket.RemoteEndPoint as IPEndPoint");
                }

                _state.LastAcceptedConnectionIp = clientEndPoint.Address;

                EventOccurred?.Invoke(this,
                    new ServerEvent
                    {
                        EventType = EventType.ConnectionAccepted,
                        RemoteServerIpAddress = _state.LastAcceptedConnectionIp
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

            var messageData = receiveMessageResult.Value;
            var messageTypeData = MessageUnwrapper.ReadInt32(messageData).ToString();
            var messageType = (MessageType)Enum.Parse(typeof(MessageType), messageTypeData);

            var requestResult = await ProcessRequestAsync(messageType, messageData, token).ConfigureAwait(false);

            //if (_state.FileTransferCanceled)
            //{
            //    var cancelFileTransfer = await CancelStalledFileTransfer(
            //        _state.ClientEndPoint.Address.ToString(),
            //        _state.ClientEndPoint.Port,
            //        _MyLocalIpAddress,
            //        _MyServerPort,
            //        token);

            //    _state.FileTransferCanceled = false;

            //    if (cancelFileTransfer.Failure) return cancelFileTransfer;
            //}

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

        async Task<Result<byte[]>> ReceiveMessageFromClientAsync()
        {
            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceiveMessageFromClientStarted,
                    RemoteServerIpAddress = _state.LastAcceptedConnectionIp
                });

            _state.Buffer = new byte[_state.BufferSize];
            _state.UnreadBytes = new List<byte>();

            var messageLengthResult = await DetermineMessageLengthAsync().ConfigureAwait(false);
            if (messageLengthResult.Failure)
            {
                return Result.Fail<byte[]>(messageLengthResult.Error);
            }

            var messageLength = messageLengthResult.Value;

            var receiveMessageResult = await ReceiveAllMessageBytesAsync(messageLength).ConfigureAwait(false);
            if (receiveMessageResult.Failure)
            {
                return receiveMessageResult;
            }

            var messageData = receiveMessageResult.Value;

            EventOccurred?.Invoke(this,
                new ServerEvent
                    {EventType = EventType.ReceiveMessageFromClientComplete});

            return Result.Ok(messageData);
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
                        _state.BufferSize,
                        SocketFlags.None,
                        SocketSettings.ReceiveTimeoutMs)
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
        
        async Task<Result> ProcessRequestAsync(MessageType messageType, byte[] messageData, CancellationToken token)
        {
            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ProcessRequestStarted,
                    MessageType = messageType
                });

            switch (messageType)
            {
                case MessageType.TextMessage:
                    ReceiveTextMessage(messageData);
                    break;

                case MessageType.InboundFileTransfer:
                   await InboundFileTransferAsync(messageData, token).ConfigureAwait(false);
                    break;

                case MessageType.OutboundFileTransfer:
                    await OutboundFileTransferAsync(messageData).ConfigureAwait(false);
                    break;

                case MessageType.FileTransferAccepted:
                    await HandleFileTransferAcceptedAsync(messageData, token);
                    break;

                case MessageType.FileTransferStalled:
                    HandleStalledFileTransfer(messageData);
                    break;

                case MessageType.RetryOutboundFileTransfer:
                    await HandleRequestRetryCanceledFileTransferAsync(messageData);
                    break;

                case MessageType.FileListRequest:
                    await SendFileListAsync(messageData).ConfigureAwait(false);
                    break;

                case MessageType.FileList:
                    ReceiveFileList(messageData);
                    break;

                case MessageType.TransferFolderPathRequest:
                    await SendTransferFolderPathAsync(messageData).ConfigureAwait(false);
                    break;

                case MessageType.TransferFolderPath:
                    ReceiveTransferFolderPath(messageData);
                    break;

                case MessageType.PublicIpAddressRequest:
                    await SendPublicIpAsync(messageData).ConfigureAwait(false);
                    break;

                case MessageType.PublicIpAddress:
                    ReceivePublicIpAddress(messageData);
                    break;

                case MessageType.NoFilesAvailableForDownload:
                    HandleNoFilesAvailableForDownload(messageData);
                    break;

                case MessageType.FileTransferRejected:
                    HandleFileTransferRejected(messageData);
                    break;

                //case MessageType.FileTransferCanceled:
                //    return HandleCanceledFileTransfer(messageData);

                case MessageType.RequestedFolderDoesNotExist:
                    HandleRequestedFolderDoesNotExist(messageData);
                    break;

                case MessageType.ShutdownServerCommand:
                    HandleShutdownServerCommand(messageData);
                    break;

                default:
                    var error = $"Unable to determine transfer type, value of '{messageType}' is invalid.";
                    return Result.Fail(error);
            }

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ProcessRequestComplete,
                    MessageType = messageType
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

            var newClient = new RemoteServer(remoteServerIpAddress, remoteServerPort);
            _state.ClientInfo = newClient.ConnectionInfo;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.SendTextMessageStarted,
                    TextMessage = message,
                    RemoteServerIpAddress = ClientSessionIpAddress,
                    RemoteServerPortNumber = ClientServerPort
                });

            var connectResult =
                await ConnectToServerAsync();

            if (connectResult.Failure)
            {
                return connectResult;
            }

            var messageData =
                MessageWrapper.ConstuctTextMessageRequest(message, MyLocalIpAddress.ToString(), MyServerPort);

            var sendMessageDataResult = await SendMessageData(messageData);
            if (sendMessageDataResult.Failure)
            {
                return sendMessageDataResult;
            }

            Close_serverSocket();

            EventOccurred?.Invoke(this,
                new ServerEvent
                    {EventType = EventType.SendTextMessageComplete});

            return Result.Ok();
        }

        void ReceiveTextMessage(byte[] messageData)
        {
            var (message,
                remoteIpAddress,
                remotePortNumber) = MessageUnwrapper.ReadTextMessage(messageData);

            var newClient = new RemoteServer(remoteIpAddress, remotePortNumber);
            _state.ClientInfo = newClient.ConnectionInfo;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceivedTextMessage,
                    TextMessage = message,
                    RemoteServerIpAddress = ClientSessionIpAddress,
                    RemoteServerPortNumber = ClientServerPort
                });
        }

        async Task<Result> OutboundFileTransferAsync(byte[] messageData)
        {
            var (requestedFilePath,
                remoteServerIpAddress,
                remoteServerPort,
                remoteFolderPath) = MessageUnwrapper.ReadOutboundFileTransferRequest(messageData);

            var newClient = new RemoteServer(remoteServerIpAddress, remoteServerPort);
            _state.ClientInfo = newClient.ConnectionInfo;
            _state.OutgoingFilePath = requestedFilePath;
            _state.ClientTransferFolderPath = remoteFolderPath;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceivedOutboundFileTransferRequest,
                    LocalFolder = Path.GetDirectoryName(_state.OutgoingFilePath),
                    FileName = Path.GetFileName(_state.OutgoingFilePath),
                    FileSizeInBytes = new FileInfo(_state.OutgoingFilePath).Length,
                    RemoteFolder = ClientTransferFolderPath,
                    RemoteServerIpAddress = ClientSessionIpAddress,
                    RemoteServerPortNumber = ClientServerPort
                });

            if (!File.Exists(requestedFilePath))
            {
                //TODO: Add another event sequence here
                return Result.Fail("File does not exist: " + requestedFilePath);
            }

            return
                await SendFileAsync(
                    ClientSessionIpAddress,
                    ClientServerPort,
                    _state.OutgoingFilePath,
                    ClientTransferFolderPath).ConfigureAwait(false);

            //if (_state.FileTransferCanceled)
            //{
            //    var cancelFileTransfer = await CancelStalledFileTransfer(
            //        remoteServerIpAddress,
            //        remoteServerPort,
            //        _MyLocalIpAddress,
            //        _MyServerPort,
            //        token);

            //    _state.FileTransferCanceled = false;

            //    if (cancelFileTransfer.Failure) return cancelFileTransfer;
            //}            
        }

        public async Task<Result> SendFileAsync(
            IPAddress remoteServerIpAddress,
            int remoteServerPort,
            string localFilePath,
            string remoteFolderPath)
        {
            var newClient = new RemoteServer(remoteServerIpAddress, remoteServerPort);
            _state.ClientInfo = newClient.ConnectionInfo;
            _state.OutgoingFilePath = localFilePath;
            _state.ClientTransferFolderPath = remoteFolderPath;

            await Task.Delay(1);

            return await SendOutboundFileTransferRequestAsync(
                ClientSessionIpAddress,
                ClientServerPort,
                _state.OutgoingFilePath,
                ClientTransferFolderPath);
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
            var newClient = new RemoteServer(remoteServerIpAddress, remoteServerPort);
            _state.ClientInfo = newClient.ConnectionInfo;
            _state.OutgoingFilePath = localFilePath;
            _state.ClientTransferFolderPath = remoteFolderPath;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.RequestOutboundFileTransferStarted,
                    LocalIpAddress = MyLocalIpAddress,
                    LocalPortNumber = MyServerPort,
                    LocalFolder = Path.GetDirectoryName(_state.OutgoingFilePath),
                    FileName = Path.GetFileName(_state.OutgoingFilePath),
                    FileSizeInBytes = _state.OutgoingFileSize,
                    RemoteServerIpAddress = ClientSessionIpAddress,
                    RemoteServerPortNumber = ClientServerPort,
                    RemoteFolder = ClientTransferFolderPath
                });

            var connectResult =
                await ConnectToServerAsync();

            if (connectResult.Failure)
            {
                return connectResult;
            }

            var messageData =
                MessageWrapper.ConstructOutboundFileTransferRequest(
                    _state.OutgoingFilePath,
                    _state.OutgoingFileSize,
                    MyLocalIpAddress.ToString(),
                    MyServerPort,
                    ClientTransferFolderPath);

            var sendMessageDataResult = await SendMessageData(messageData);
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
            (string requestorIpAddress,
                int requestorPortNumber) = MessageUnwrapper.ReadServerConnectionInfo(messageData);

            var newClient = new RemoteServer(requestorIpAddress, requestorPortNumber);
            _state.ClientInfo = newClient.ConnectionInfo;

            //TODO: Investigate why this causes a bug that fails my unit tests
            //_state.OutgoingFilePath = string.Empty;
            //ClientTransferFolderPath = string.Empty;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ClientRejectedFileTransfer,
                    RemoteServerIpAddress = ClientSessionIpAddress,
                    RemoteServerPortNumber = ClientServerPort
                });
        }

        async Task<Result> HandleFileTransferAcceptedAsync(byte[] messageData, CancellationToken token)
        {
            (string requestorIpAddress,
                int requestorPortNumber) = MessageUnwrapper.ReadServerConnectionInfo(messageData);

            var newClient = new RemoteServer(requestorIpAddress, requestorPortNumber);
            _state.ClientInfo = newClient.ConnectionInfo;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ClientAcceptedFileTransfer,
                    RemoteServerIpAddress = ClientSessionIpAddress,
                    RemoteServerPortNumber = ClientServerPort
                });

            //TODO: Investigate why ReSharper says method will execute sunchronously without the delay below, SendFileBytes is truly async but quick action says await can be omitted
            await Task.Delay(1);

            return await SendFileBytesAsync(_state.OutgoingFilePath, token);

            //if (_state.FileTransferCanceled)
            //{
            //    var cancelFileTransfer = 
            //        await CancelStalledFileTransfer(
            //            requestorIpAddress,
            //            requestorPortNumber,
            //            _MyLocalIpAddress,
            //            _MyServerPort,
            //            token);

            //    _state.FileTransferCanceled = false;
            //    if (cancelFileTransfer.Failure) return cancelFileTransfer;
            //}
        }

        async Task<Result> SendFileBytesAsync(string localFilePath, CancellationToken token)
        {
            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.SendFileBytesStarted,
                    RemoteServerIpAddress = ClientSessionIpAddress,
                    RemoteServerPortNumber = ClientServerPort
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

                    var fileChunkSize = (int) Math.Min(_state.BufferSize, bytesRemaining);
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
                                _state.SendTimeoutMs).ConfigureAwait(false);

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

                    if (_state.OutgoingFileSize > (10 * _state.BufferSize)) continue;
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
                        RemoteServerIpAddress = ClientSessionIpAddress,
                        RemoteServerPortNumber = ClientServerPort
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
            var buffer = new byte[_state.BufferSize];
            Result<int> receiveMessageResult;
            int bytesReceived;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceiveConfirmationMessageStarted,
                    RemoteServerIpAddress = ClientSessionIpAddress,
                    RemoteServerPortNumber = ClientServerPort
                });

            try
            {
                receiveMessageResult =
                    await _serverSocket.ReceiveAsync(
                        buffer,
                        0,
                        _state.BufferSize,
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
                    RemoteServerIpAddress = ClientSessionIpAddress,
                    RemoteServerPortNumber = ClientServerPort,
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
            var newClient = new RemoteServer(remoteServerIpAddress, remoteServerPort);
            _state.ClientInfo = newClient.ConnectionInfo;
            _state.RemoteFilePath = remoteFilePath;
            _state.ClientTransferFolderPath = Path.GetDirectoryName(remoteFilePath);
            _state.MyTransferFolderPath = localFolderPath;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.RequestInboundFileTransferStarted,
                    RemoteServerIpAddress = ClientSessionIpAddress,
                    RemoteServerPortNumber = ClientServerPort,
                    RemoteFolder = ClientTransferFolderPath,
                    FileName = Path.GetFileName(_state.RemoteFilePath),
                    LocalIpAddress = MyLocalIpAddress,
                    LocalPortNumber = MyServerPort,
                    LocalFolder = MyTransferFolderPath,
                });

            var connectResult =
                await ConnectToServerAsync();

            if (connectResult.Failure)
            {
                return connectResult;
            }

            var messageData =
                MessageWrapper.ConstructInboundFileTransferRequest(
                    _state.RemoteFilePath,
                    MyLocalIpAddress.ToString(),
                    MyServerPort,
                    MyTransferFolderPath);

            var sendMessageDataResult = await SendMessageData(messageData);
            if (sendMessageDataResult.Failure)
            {
                return sendMessageDataResult;
            }

            Close_serverSocket();

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

            var newClient = new RemoteServer(remoteServerIpAddress, remoteServerPort);
            _state.ClientInfo = newClient.ConnectionInfo;
            _state.IncomingFilePath = localFilePath;
            _state.IncomingFileSize = fileSizeBytes;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceivedInboundFileTransferRequest,
                    LocalFolder = Path.GetDirectoryName(_state.IncomingFilePath),
                    FileName = Path.GetFileName(_state.IncomingFilePath),
                    FileSizeInBytes = _state.IncomingFileSize,
                    RemoteServerIpAddress = ClientSessionIpAddress,
                    RemoteServerPortNumber = ClientServerPort
                });

            if (File.Exists(_state.IncomingFilePath))
            {
                return
                    await SendSimpleMessageToClientAsync(
                        MessageType.FileTransferRejected,
                        EventType.SendFileTransferRejectedStarted,
                        EventType.SendFileTransferRejectedComplete);
            }

            var acceptTransferResult =
                await SendSimpleMessageToClientAsync(
                    MessageType.FileTransferAccepted,
                    EventType.SendFileTransferAcceptedStarted,
                    EventType.SendFileTransferAcceptedComplete);

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
                await ConfirmFileTransferComplete();

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
                    RemoteServerIpAddress = ClientSessionIpAddress,
                    RemoteServerPortNumber = ClientServerPort,
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
                        _state.IncomingFilePath,
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
                        _state.IncomingFilePath,
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
                if (_state.IncomingFileSize < (10 * _state.BufferSize))
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
                    RemoteServerIpAddress = ClientSessionIpAddress,
                    RemoteServerPortNumber = ClientServerPort
                });

            return Result.Ok();
        }

        public async Task<Result> SendNotificationFileTransferStalledAsync()
        {
            _state.FileTransferStalled = true;

            await Task.Delay(1);

            return
                await SendSimpleMessageToClientAsync(
                    MessageType.FileTransferStalled,
                    EventType.SendFileTransferStalledStarted,
                    EventType.SendFileTransferStalledComplete);
        }

        void HandleStalledFileTransfer(byte[] messageData)
        {
            (string requestorIpAddress,
                int requestorPortNumber) = MessageUnwrapper.ReadServerConnectionInfo(messageData);

            var newClient = new RemoteServer(requestorIpAddress, requestorPortNumber);
            _state.ClientInfo = newClient.ConnectionInfo;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.FileTransferStalled,
                    RemoteServerIpAddress = ClientSessionIpAddress,
                    RemoteServerPortNumber = ClientServerPort
                });

            _state.FileTransferCanceled = true;
            _signalSendNextFileChunk.Set();
        }

        public async Task<Result> RetryCanceledFileTransfer(
            IPAddress remoteServerIpAddress,
            int remoteServerPort)
        {
            var newClient = new RemoteServer(remoteServerIpAddress, remoteServerPort);
            _state.ClientInfo = newClient.ConnectionInfo;

            await Task.Delay(1);

            return await SendSimpleMessageToClientAsync(
                MessageType.RetryOutboundFileTransfer,
                EventType.RetryOutboundFileTransferStarted,
                EventType.RetryOutboundFileTransferComplete);
        }

        async Task<Result> HandleRequestRetryCanceledFileTransferAsync(byte[] messageData)
        {
            (string requestorIpAddress,
                int requestorPortNumber) = MessageUnwrapper.ReadServerConnectionInfo(messageData);

            var newClient = new RemoteServer(requestorIpAddress, requestorPortNumber);
            _state.ClientInfo = newClient.ConnectionInfo;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceivedRetryOutboundFileTransferRequest,
                    RemoteServerIpAddress = ClientSessionIpAddress,
                    RemoteServerPortNumber = ClientServerPort
                });

            await Task.Delay(1);

            return
                await SendFileAsync(
                    ClientSessionIpAddress,
                    ClientServerPort,
                    _state.OutgoingFilePath,
                    ClientTransferFolderPath).ConfigureAwait(false);

            //if (_state.FileTransferCanceled)
            //{
            //    var cancelFileTransfer = await CancelStalledFileTransfer(
            //        requestorIpAddress,
            //        requestorPortNumber,
            //        _MyLocalIpAddress,
            //        _MyServerPort,
            //        token);

            //    _state.FileTransferCanceled = false;

            //    if (cancelFileTransfer.Failure) return cancelFileTransfer;
            //}            
        }

        //public async Task<Result> CancelStalledFileTransfer(
        //    string remoteServerIpAddress,
        //    int remoteServerPort,
        //    string localIpAddress,
        //    int localPort,
        //    CancellationToken token)
        //{
        //    return
        //        await SendSimpleMessageToClientAsync(
        //            MessageType.FileTransferCanceled,
        //            EventType.SendFileTransferCanceledStarted,
        //            EventType.SendFileTransferCanceledComplete,
        //            remoteServerIpAddress,
        //            remoteServerPort,
        //            localIpAddress,
        //            localPort,
        //            token);
        //}

        //Result HandleCanceledFileTransfer(byte[] messageData)
        //{
        //    EventOccurred?.Invoke(this,
        //        new ServerEvent
        //            { EventType = EventType.ReceiveFileTransferCanceledStarted });

        //    (string requestorIpAddress,
        //        int requestorPortNumber) = MessageUnwrapper.ReadServerConnectionInfo(messageData);

        //    _state.FileTransferStalled = false;

        //    EventOccurred?.Invoke(this,
        //        new ServerEvent
        //        {
        //            EventType = EventType.ReceiveFileTransferCanceledComplete,
        //            RemoteServerIpAddress = requestorIpAddress,
        //            RemoteServerPortNumber = requestorPortNumber
        //        });

        //    return Result.Ok();
        //}

        async Task<Result> ConfirmFileTransferComplete()
        {
            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.SendConfirmationMessageStarted,
                    RemoteServerIpAddress = ClientSessionIpAddress,
                    RemoteServerPortNumber = ClientServerPort,
                    ConfirmationMessage = ConfirmationMessage
                });

            var confirmationMessageData = Encoding.ASCII.GetBytes(ConfirmationMessage);

            var sendConfirmatinMessageResult =
                await _clientSocket.SendWithTimeoutAsync(
                    confirmationMessageData,
                    0,
                    confirmationMessageData.Length,
                    SocketFlags.None,
                    _state.SendTimeoutMs).ConfigureAwait(false);

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
                    RemoteServerIpAddress = ClientSessionIpAddress,
                    RemoteServerPortNumber = ClientServerPort,
                    LocalIpAddress = MyLocalIpAddress,
                    LocalPortNumber = MyServerPort
                });

            var connectResult =
                await ConnectToServerAsync();

            if (connectResult.Failure)
            {
                return connectResult;
            }

            var messageData =
                MessageWrapper.ConstructGenericMessage(messageType, MyLocalIpAddress.ToString(), MyServerPort);

            var sendMessageDataResult = await SendMessageData(messageData);
            if (sendMessageDataResult.Failure)
            {
                return sendMessageDataResult;
            }

            Close_serverSocket();

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = sendMessageCompleteEventType,
                    RemoteServerIpAddress = ClientSessionIpAddress,
                    RemoteServerPortNumber = ClientServerPort,
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
                    RemoteServerIpAddress = ClientSessionIpAddress,
                    RemoteServerPortNumber = ClientServerPort
                });

            var connectResult =
                await _serverSocket.ConnectWithTimeoutAsync(
                    ClientSessionIpAddress,
                    ClientServerPort,
                    _state.ConnectTimeoutMs).ConfigureAwait(false);

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
                    _state.SendTimeoutMs).ConfigureAwait(false);

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
                    _state.SendTimeoutMs).ConfigureAwait(false);

            return sendMessageResult.Success
                ? Result.Ok()
                : sendMessageResult;
        }

        void Close_serverSocket()
        {
            _serverSocket.Shutdown(SocketShutdown.Both);
            _serverSocket.Close();
        }

        public async Task<Result> RequestFileListAsync(
            string remoteServerIpAddress,
            int remoteServerPort,
            string targetFolder)
        {
            var newClient = new RemoteServer(remoteServerIpAddress, remoteServerPort);
            _state.ClientInfo = newClient.ConnectionInfo;
            _state.ClientTransferFolderPath = targetFolder;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.RequestFileListStarted,
                    LocalIpAddress = MyLocalIpAddress,
                    LocalPortNumber = MyServerPort,
                    RemoteServerIpAddress = ClientSessionIpAddress,
                    RemoteServerPortNumber = ClientServerPort,
                    RemoteFolder = ClientTransferFolderPath
                });

            var connectResult =
                await ConnectToServerAsync();

            if (connectResult.Failure)
            {
                return connectResult;
            }

            var messageData =
                MessageWrapper.ConstructFileListRequest(
                    MyLocalIpAddress.ToString(),
                    MyServerPort,
                    ClientTransferFolderPath);

            var sendMessageDataResult = await SendMessageData(messageData);
            if (sendMessageDataResult.Failure)
            {
                return sendMessageDataResult;
            }

            Close_serverSocket();

            EventOccurred?.Invoke(this,
                new ServerEvent
                    {EventType = EventType.RequestFileListComplete});

            return Result.Ok();
        }

        async Task<Result> SendFileListAsync(byte[] messageData)
        {
            (string requestorIpAddress,
                int requestorPortNumber,
                string targetFolderPath) = MessageUnwrapper.ReadFileListRequest(messageData);

            var newClient = new RemoteServer(requestorIpAddress, requestorPortNumber);
            _state.ClientInfo = newClient.ConnectionInfo;
            _state.MyTransferFolderPath = targetFolderPath;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceivedFileListRequest,
                    RemoteServerIpAddress = ClientSessionIpAddress,
                    RemoteServerPortNumber = ClientServerPort,
                    RemoteFolder = MyTransferFolderPath
                });

            if (!Directory.Exists(MyTransferFolderPath))
            {
                return
                    await SendSimpleMessageToClientAsync(
                        MessageType.RequestedFolderDoesNotExist,
                        EventType.SendNotificationFolderDoesNotExistStarted,
                        EventType.SendNotificationFolderDoesNotExistComplete);
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
                    EventType.SendNotificationNoFilesToDownloadComplete);
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
                    RemoteServerIpAddress = ClientSessionIpAddress,
                    RemoteServerPortNumber = ClientServerPort,
                    FileInfoList = fileInfoList,
                    LocalFolder = MyTransferFolderPath,
                });

            var connectResult =
                await ConnectToServerAsync();

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

            var sendMessageDataResult = await SendMessageData(responseData);
            if (sendMessageDataResult.Failure)
            {
                return sendMessageDataResult;
            }

            Close_serverSocket();

            EventOccurred?.Invoke(this,
                new ServerEvent
                    {EventType = EventType.SendFileListComplete});

            return Result.Ok();
        }

        void HandleRequestedFolderDoesNotExist(byte[] messageData)
        {
            (string requestorIpAddress,
                int requestorPortNumber) = MessageUnwrapper.ReadServerConnectionInfo(messageData);

            var newClient = new RemoteServer(requestorIpAddress, requestorPortNumber);
            _state.ClientInfo = newClient.ConnectionInfo;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceivedNotificationFolderDoesNotExist,
                    RemoteServerIpAddress = ClientSessionIpAddress,
                    RemoteServerPortNumber = ClientServerPort
                });
        }

        void HandleNoFilesAvailableForDownload(byte[] messageData)
        {
            (string requestorIpAddress,
                int requestorPortNumber) = MessageUnwrapper.ReadServerConnectionInfo(messageData);

            var newClient = new RemoteServer(requestorIpAddress, requestorPortNumber);
            _state.ClientInfo = newClient.ConnectionInfo;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceivedNotificationNoFilesToDownload,
                    RemoteServerIpAddress = ClientSessionIpAddress,
                    RemoteServerPortNumber = ClientServerPort
                });
        }

        void ReceiveFileList(byte[] messageData)
        {
            var (remoteServerIp,
                remoteServerPort,
                transferFolder,
                fileInfoList) = MessageUnwrapper.ReadFileListResponse(messageData);

            var newClient = new RemoteServer(remoteServerIp, remoteServerPort);
            _state.ClientInfo = newClient.ConnectionInfo;
            _state.ClientTransferFolderPath = transferFolder;

            _state.FileListInfo = fileInfoList;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceivedFileList,
                    LocalIpAddress = MyLocalIpAddress,
                    LocalPortNumber = MyServerPort,
                    RemoteServerIpAddress = ClientSessionIpAddress,
                    RemoteServerPortNumber = ClientServerPort,
                    RemoteFolder = ClientTransferFolderPath,
                    FileInfoList = FileListInfo 
                });
        }

        public async Task<Result> RequestTransferFolderPathAsync(
            string remoteServerIpAddress,
            int remoteServerPort)
        {
            var newClient = new RemoteServer(remoteServerIpAddress, remoteServerPort);
            _state.ClientInfo = newClient.ConnectionInfo;

            return
                await SendSimpleMessageToClientAsync(
                    MessageType.TransferFolderPathRequest,
                    EventType.RequestTransferFolderPathStarted,
                    EventType.RequestTransferFolderPathComplete);
        }

        async Task<Result> SendTransferFolderPathAsync(byte[] messageData)
        {
            (string requestorIpAddress,
                int requestorPortNumber) = MessageUnwrapper.ReadServerConnectionInfo(messageData);

            var newClient = new RemoteServer(requestorIpAddress, requestorPortNumber);
            _state.ClientInfo = newClient.ConnectionInfo;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceivedTransferFolderPathRequest,
                    RemoteServerIpAddress = ClientSessionIpAddress,
                    RemoteServerPortNumber = ClientServerPort
                });

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.SendTransferFolderPathStarted,
                    RemoteServerIpAddress = ClientSessionIpAddress,
                    RemoteServerPortNumber = ClientServerPort,
                    LocalFolder = MyTransferFolderPath
                });

            var connectResult =
                await ConnectToServerAsync();

            if (connectResult.Failure)
            {
                return connectResult;
            }

            var responseData =
                MessageWrapper.ConstructTransferFolderResponse(
                    MyLocalIpAddress.ToString(),
                    MyServerPort,
                    MyTransferFolderPath);

            var sendMessageDataResult = await SendMessageData(responseData);
            if (sendMessageDataResult.Failure)
            {
                return sendMessageDataResult;
            }

            Close_serverSocket();

            EventOccurred?.Invoke(this,
                new ServerEvent
                    {EventType = EventType.SendTransferFolderPathComplete });

            return Result.Ok();
        }

        void ReceiveTransferFolderPath(byte[] messageData)
        {
            var (remoteServerIp,
                remoteServerPort,
                transferFolder) = MessageUnwrapper.ReadTransferFolderResponse(messageData);

            var newClient = new RemoteServer(remoteServerIp, remoteServerPort);
            _state.ClientInfo = newClient.ConnectionInfo;
            _state.ClientTransferFolderPath = transferFolder;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceivedTransferFolderPath,
                    RemoteServerIpAddress = ClientSessionIpAddress,
                    RemoteServerPortNumber = ClientServerPort,
                    RemoteFolder = ClientTransferFolderPath
                });
        }

        public async Task<Result> RequestPublicIpAsync(
            string remoteServerIpAddress,
            int remoteServerPort)
        {
            var newClient = new RemoteServer(remoteServerIpAddress, remoteServerPort);
            _state.ClientInfo = newClient.ConnectionInfo;

            await Task.Delay(1);

            return
                await SendSimpleMessageToClientAsync(
                    MessageType.PublicIpAddressRequest,
                    EventType.RequestPublicIpAddressStarted,
                    EventType.RequestPublicIpAddressComplete);
        }

        async Task<Result> SendPublicIpAsync(byte[] messageData)
        {
            (string requestorIpAddress,
                int requestorPortNumber) = MessageUnwrapper.ReadServerConnectionInfo(messageData);

            var newClient = new RemoteServer(requestorIpAddress, requestorPortNumber);
            _state.ClientInfo = newClient.ConnectionInfo;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceivedPublicIpAddressRequest,
                    RemoteServerIpAddress = ClientSessionIpAddress,
                    RemoteServerPortNumber = ClientServerPort
                });

            var publicIpResult = await Network.GetPublicIPv4AddressAsync().ConfigureAwait(false);
            if (publicIpResult.Failure)
            {
                return Result.Fail(publicIpResult.Error);
            }

            _state.MyInfo.PublicIpAddress = publicIpResult.Value;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.SendPublicIpAddressStarted,
                    RemoteServerIpAddress = ClientSessionIpAddress,
                    RemoteServerPortNumber = ClientServerPort,
                    LocalIpAddress = MyLocalIpAddress,
                    LocalPortNumber = MyServerPort,
                    PublicIpAddress = MyPublicIpAddress
                });

            var connectResult =
                await ConnectToServerAsync();

            if (connectResult.Failure)
            {
                return connectResult;
            }

            var responseData =
                MessageWrapper.ConstructPublicIpAddressResponse(
                    MyLocalIpAddress.ToString(),
                    MyServerPort,
                    MyPublicIpAddress.ToString());

            var sendMessageDataResult = await SendMessageData(responseData);
            if (sendMessageDataResult.Failure)
            {
                return sendMessageDataResult;
            }

            Close_serverSocket();

            EventOccurred?.Invoke(this,
                new ServerEvent
                    {EventType = EventType.SendPublicIpAddressComplete});

            return Result.Ok();
        }

        void ReceivePublicIpAddress(byte[] messageData)
        {
            var (remoteServerIp,
                remoteServerPort,
                publicIpAddress) = MessageUnwrapper.ReadPublicIpAddressResponse(messageData);

            var newClient = new RemoteServer(remoteServerIp, remoteServerPort);
            _state.ClientInfo = newClient.ConnectionInfo;
            ClientInfo.PublicIpAddress = Network.ParseSingleIPv4Address(publicIpAddress).Value;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceivedPublicIpAddress,
                    RemoteServerIpAddress = ClientSessionIpAddress,
                    RemoteServerPortNumber = ClientServerPort,
                    PublicIpAddress = ClientPublicIpAddress
                });
        }

        public async Task<Result> ShutdownServerAsync()
        {
            if (ShutdownComplete)
            {
                return Result.Fail("Server is already shutdown");
            }
            
            _state.ClientInfo = _state.MyInfo;

            var shutdownResult =
                await SendSimpleMessageToClientAsync(
                    MessageType.ShutdownServerCommand,
                    EventType.SendShutdownServerCommandStarted,
                    EventType.SendShutdownServerCommandComplete);

            return shutdownResult.Success
                ? Result.Ok()
                : Result.Fail($"Error occurred shutting down the server + {shutdownResult.Error}");
        }

        void HandleShutdownServerCommand(byte[] messageData)
        {
            (string requestorIpAddress,
                int requestorPortNumber) = MessageUnwrapper.ReadServerConnectionInfo(messageData);

            var newClient = new RemoteServer(requestorIpAddress, requestorPortNumber);
            _state.ClientInfo = newClient.ConnectionInfo;

            var ipComparison =
                Network.CompareTwoIpAddresses(_state.LastAcceptedConnectionIp, MyLocalIpAddress);

            var ipsMatch = ipComparison == IpAddressSimilarity.AllBytesMatch;
            var portsMatch = ClientServerPort == MyServerPort;
            if (ipsMatch && portsMatch)
            {
                ReceivedShutdownCommand = true;
            }

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceivedShutdownServerCommand,
                    RemoteServerIpAddress = ClientSessionIpAddress,
                    RemoteServerPortNumber = ClientServerPort
                });
        }

        public Result ShutdownListenSocket()
        {
            //_cts.Cancel();
            //await Task.Delay(500);

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

                return Result.Fail(errorMessage);
            }

            EventOccurred?.Invoke(this,
                new ServerEvent
                    {EventType = EventType.ShutdownListenSocketCompletedWithoutError});

            return Result.Ok();
        }
    }
}