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

        readonly AutoResetEvent _signalSendNextFileChunk = new AutoResetEvent(true);
        readonly Logger _log = new Logger(typeof(TplSocketServer));
        CancellationTokenSource _cts;

        int _receivedShutdownCommand;
        int _shutdownComplete;

        public TplSocketServer(IPAddress localIpAddress, int port)
        {
            State = new ServerState(localIpAddress, port);
            LogFileName = "server.log";
        }

        public ServerState State { get; }

        public bool LoggingEnabled
        {
            get => State.LoggingEnabled;
            set => State.LoggingEnabled = value;
        }

        public string LogFileName { get; set; }

        public string TransferFolderPath
        {
            get => State.MyTransferFolderPath;
            set => State.MyTransferFolderPath = value;
        }

        public float TransferUpdateInterval
        {
            get => State.TransferUpdateInterval;
            set => State.TransferUpdateInterval = value;
        }

        public SocketSettings SocketSettings
        {
            get => State.SocketSettings;
            set => State.SocketSettings = value;
        }

        public event EventHandler<ServerEvent> EventOccurred;
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

            Logger.Start(LogFileName);

            var listenResult = Listen(string.Empty, State.MyServerPort);
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
                State.ListenSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                State.ListenSocket.Bind(ipEndPoint);
                State.ListenSocket.Listen(State.SocketSettings.MaxNumberOfConections);
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
                    LocalPortNumber = State.MyServerPort
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
                var acceptResult = await State.ListenSocket.AcceptTaskAsync(token).ConfigureAwait(false);

                if (acceptResult.Failure)
                {
                    return acceptResult;
                }

                if (token.IsCancellationRequested)
                {
                    return Result.Ok();
                }

                State.ClientSocket = acceptResult.Value;

                if (!(State.ClientSocket.RemoteEndPoint is IPEndPoint clientEndPoint))
                {
                    return Result.Fail("Error occurred casting _state.ClientSocket.RemoteEndPoint as IPEndPoint");
                }

                State.LastAcceptedConnectionIp = clientEndPoint.Address;

                EventOccurred?.Invoke(this,
                    new ServerEvent
                    {
                        EventType = EventType.ConnectionAccepted,
                        RemoteServerIpAddress = State.LastAcceptedConnectionIp.ToString()
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
            //        _state.MyLocalIpAddress,
            //        _state.MyServerPort,
            //        token);

            //    _state.FileTransferCanceled = false;

            //    if (cancelFileTransfer.Failure) return cancelFileTransfer;
            //}

            try
            {
                State.ClientSocket.Shutdown(SocketShutdown.Both);
                State.ClientSocket.Close();
            }
            catch (SocketException ex)
            {
                _log.Error("Error raised in method ShutdownListenSocket", ex);
                return Result.Fail($"{ex.Message} ({ex.GetType()} raised in method TplSocketServer.ShutdownListenSocket)");
            }

            return requestResult;
        }

        async Task<Result<byte[]>> ReceiveMessageFromClientAsync()
        {
            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceiveMessageFromClientStarted,
                    RemoteServerIpAddress = State.LastAcceptedConnectionIp.ToString()
                });

            State.Buffer = new byte[State.BufferSize];
            State.UnreadBytes = new List<byte>();

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
                    await State.ClientSocket.ReceiveWithTimeoutAsync(
                        State.Buffer,
                        0,
                        State.BufferSize,
                        0,
                        State.SocketSettings.ReceiveTimeoutMs).ConfigureAwait(false);
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

            State.LastBytesReceivedCount = readFromSocketResult.Value;
            var numberOfUnreadBytes = State.LastBytesReceivedCount - 4;
            var messageLength = MessageUnwrapper.ReadInt32(State.Buffer);

            SocketEventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceivedMessageLengthFromSocket,
                    BytesReceived = State.LastBytesReceivedCount,
                    MessageLengthInBytes = 4,
                    UnreadByteCount = numberOfUnreadBytes
                });

            if (State.LastBytesReceivedCount > 4)
            {
                var unreadBytes = new byte[numberOfUnreadBytes];
                State.Buffer.ToList().CopyTo(4, unreadBytes, 0, numberOfUnreadBytes);
                State.UnreadBytes = unreadBytes.ToList();

                EventOccurred?.Invoke(this,
                    new ServerEvent
                    {
                        EventType = EventType.SaveUnreadBytesAfterReceiveMessageLength,
                        CurrentMessageBytesReceived = State.LastBytesReceivedCount,
                        ExpectedByteCount = 4,
                        UnreadByteCount = numberOfUnreadBytes,
                    });
            }

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.DetermineMessageLengthComplete,
                    MessageLengthInBytes = messageLength
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

            if (State.UnreadBytes.Count > 0)
            {
                messageByteCount = Math.Min(messageLength, State.UnreadBytes.Count);
                var messageBytes = new byte[messageByteCount];

                State.UnreadBytes.CopyTo(0, messageBytes, 0, messageByteCount);
                messageData.AddRange(messageBytes.ToList());

                totalBytesReceived += messageByteCount;
                bytesRemaining -= messageByteCount;

                EventOccurred?.Invoke(this,
                    new ServerEvent
                    {
                        EventType = EventType.CopySavedBytesToMessageData,
                        UnreadByteCount = State.UnreadBytes.Count,
                        TotalMessageBytesReceived = messageByteCount,
                        MessageLengthInBytes = messageLength,
                        MessageBytesRemaining = bytesRemaining
                    });

                if (State.UnreadBytes.Count > messageLength)
                {
                    var fileByteCount = State.UnreadBytes.Count - messageLength;
                    var fileBytes = new byte[fileByteCount];
                    State.UnreadBytes.CopyTo(messageLength, fileBytes, 0, fileByteCount);
                    State.UnreadBytes = fileBytes.ToList();
                }
                else
                {
                    State.UnreadBytes = new List<byte>();
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

                State.LastBytesReceivedCount = readFromSocketResult.Value;

                messageByteCount = Math.Min(bytesRemaining, State.LastBytesReceivedCount);
                var receivedBytes = new byte[messageByteCount];

                State.Buffer.ToList().CopyTo(0, receivedBytes, 0, messageByteCount);
                messageData.AddRange(receivedBytes.ToList());

                socketReadCount++;
                newUnreadByteCount = State.LastBytesReceivedCount - messageByteCount;
                totalBytesReceived += messageByteCount;
                bytesRemaining -= messageByteCount;

                SocketEventOccurred?.Invoke(this,
                    new ServerEvent
                    {
                        EventType = EventType.ReceivedMessageBytesFromSocket,
                        SocketReadCount = socketReadCount,
                        BytesReceived = State.LastBytesReceivedCount,
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
                State.Buffer.ToList().CopyTo(messageByteCount, unreadBytes, 0, newUnreadByteCount);
                State.UnreadBytes = unreadBytes.ToList();

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
                    {EventType = EventType.ReceiveMessageBytesComplete});

            return Result.Ok(messageData.ToArray());
        }

        public event EventHandler<ServerEvent> SocketEventOccurred;

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
            State.ClientInfo = newClient.ConnectionInfo;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.SendTextMessageStarted,
                    TextMessage = message,
                    RemoteServerIpAddress = State.ClientSessionIpAddress,
                    RemoteServerPortNumber = State.ClientServerPort
                });

            var connectResult =
                await ConnectToServerAsync();

            if (connectResult.Failure)
            {
                return connectResult;
            }

            var messageData =
                MessageWrapper.ConstuctTextMessageRequest(message, State.MyLocalIpAddress, State.MyServerPort);

            var sendMessageDataResult = await SendMessageData(messageData);
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

        Result ReceiveTextMessage(byte[] messageData)
        {
            var (message,
                remoteIpAddress,
                remotePortNumber) = MessageUnwrapper.ReadTextMessage(messageData);

            var newClient = new RemoteServer(remoteIpAddress, remotePortNumber);
            State.ClientInfo = newClient.ConnectionInfo;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceivedTextMessage,
                    TextMessage = message,
                    RemoteServerIpAddress = State.ClientSessionIpAddress,
                    RemoteServerPortNumber = State.ClientServerPort
                });

            return Result.Ok();
        }

        async Task<Result> OutboundFileTransferAsync(byte[] messageData)
        {
            var (requestedFilePath,
                remoteServerIpAddress,
                remoteServerPort,
                remoteFolderPath) = MessageUnwrapper.ReadOutboundFileTransferRequest(messageData);

            var newClient = new RemoteServer(remoteServerIpAddress, remoteServerPort);
            State.ClientInfo = newClient.ConnectionInfo;
            State.OutgoingFilePath = requestedFilePath;
            State.ClientTransferFolderPath = remoteFolderPath;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceivedOutboundFileTransferRequest,
                    LocalFolder = Path.GetDirectoryName(State.OutgoingFilePath),
                    FileName = Path.GetFileName(State.OutgoingFilePath),
                    FileSizeInBytes = new FileInfo(State.OutgoingFilePath).Length,
                    RemoteFolder = State.ClientTransferFolderPath,
                    RemoteServerIpAddress = State.ClientSessionIpAddress,
                    RemoteServerPortNumber = State.ClientServerPort
                });

            if (!File.Exists(requestedFilePath))
            {
                //TODO: Add another event sequence here
                return Result.Fail("File does not exist: " + requestedFilePath);
            }

            return
                await SendFileAsync(
                    State.ClientSessionIpAddress,
                    State.ClientServerPort,
                    State.OutgoingFilePath,
                    State.ClientTransferFolderPath).ConfigureAwait(false);

            //if (_state.FileTransferCanceled)
            //{
            //    var cancelFileTransfer = await CancelStalledFileTransfer(
            //        remoteServerIpAddress,
            //        remoteServerPort,
            //        _state.MyLocalIpAddress,
            //        _state.MyServerPort,
            //        token);

            //    _state.FileTransferCanceled = false;

            //    if (cancelFileTransfer.Failure) return cancelFileTransfer;
            //}            
        }

        public async Task<Result> SendFileAsync(
            string remoteServerIpAddress,
            int remoteServerPort,
            string localFilePath,
            string remoteFolderPath)
        {
            var newClient = new RemoteServer(remoteServerIpAddress, remoteServerPort);
            State.ClientInfo = newClient.ConnectionInfo;
            State.OutgoingFilePath = localFilePath;
            State.ClientTransferFolderPath = remoteFolderPath;

            await Task.Delay(1);

            return await SendOutboundFileTransferRequestAsync(
                State.ClientSessionIpAddress,
                State.ClientServerPort,
                State.OutgoingFilePath,
                State.ClientTransferFolderPath);
        }

        async Task<Result> SendOutboundFileTransferRequestAsync(
            string remoteServerIpAddress,
            int remoteServerPort,
            string localFilePath,
            string remoteFolderPath)
        {
            if (!File.Exists(localFilePath))
            {
                return Result.Fail("File does not exist: " + localFilePath);
            }
            var newClient = new RemoteServer(remoteServerIpAddress, remoteServerPort);
            State.ClientInfo = newClient.ConnectionInfo;
            State.OutgoingFilePath = localFilePath;
            State.ClientTransferFolderPath = remoteFolderPath;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.RequestOutboundFileTransferStarted,
                    LocalIpAddress = State.MyLocalIpAddress,
                    LocalPortNumber = State.MyServerPort,
                    LocalFolder = Path.GetDirectoryName(State.OutgoingFilePath),
                    FileName = Path.GetFileName(State.OutgoingFilePath),
                    FileSizeInBytes = State.OutgoingFileSize,
                    RemoteServerIpAddress = State.ClientSessionIpAddress,
                    RemoteServerPortNumber = State.ClientServerPort,
                    RemoteFolder = State.ClientTransferFolderPath
                });

            var connectResult =
                await ConnectToServerAsync();

            if (connectResult.Failure)
            {
                return connectResult;
            }

            var messageData =
                MessageWrapper.ConstructOutboundFileTransferRequest(
                    State.OutgoingFilePath,
                    State.OutgoingFileSize,
                    State.MyLocalIpAddress,
                    State.MyServerPort,
                    State.ClientTransferFolderPath);

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

        Result HandleFileTransferRejected(byte[] messageData)
        {
            (string requestorIpAddress,
                int requestorPortNumber) = MessageUnwrapper.ReadServerConnectionInfo(messageData);

            var newClient = new RemoteServer(requestorIpAddress, requestorPortNumber);
            State.ClientInfo = newClient.ConnectionInfo;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ClientRejectedFileTransfer,
                    RemoteServerIpAddress = State.ClientSessionIpAddress,
                    RemoteServerPortNumber = State.ClientServerPort
                });

            //State.OutgoingFilePath = string.Empty;
            //State.ClientTransferFolderPath = string.Empty;

            return Result.Ok();
        }

        async Task<Result> HandleFileTransferAcceptedAsync(byte[] messageData, CancellationToken token)
        {
            (string requestorIpAddress,
                int requestorPortNumber) = MessageUnwrapper.ReadServerConnectionInfo(messageData);

            var newClient = new RemoteServer(requestorIpAddress, requestorPortNumber);
            State.ClientInfo = newClient.ConnectionInfo;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ClientAcceptedFileTransfer,
                    RemoteServerIpAddress = State.ClientSessionIpAddress,
                    RemoteServerPortNumber = State.ClientServerPort
                });

            await Task.Delay(1);

            return await SendFileBytesAsync(State.OutgoingFilePath, token);

            //if (_state.FileTransferCanceled)
            //{
            //    var cancelFileTransfer = 
            //        await CancelStalledFileTransfer(
            //            requestorIpAddress,
            //            requestorPortNumber,
            //            _state.MyLocalIpAddress,
            //            _state.MyServerPort,
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
                    RemoteServerIpAddress = State.ClientSessionIpAddress,
                    RemoteServerPortNumber = State.ClientServerPort
                });

            var bytesRemaining = State.OutgoingFileSize;
            var fileChunkSentCount = 0;
            State.FileTransferCanceled = false;

            using (var file = File.OpenRead(localFilePath))
            {
                while (bytesRemaining > 0)
                {
                    if (token.IsCancellationRequested)
                    {
                        return Result.Ok();
                    }

                    var fileChunkSize = (int) Math.Min(State.BufferSize, bytesRemaining);
                    State.Buffer = new byte[fileChunkSize];

                    var numberOfBytesToSend = file.Read(State.Buffer, 0, fileChunkSize);
                    bytesRemaining -= numberOfBytesToSend;

                    var offset = 0;
                    var socketSendCount = 0;
                    while (numberOfBytesToSend > 0)
                    {
                        var sendFileChunkResult =
                            await State.ServerSocket.SendWithTimeoutAsync(
                                State.Buffer,
                                offset,
                                fileChunkSize,
                                SocketFlags.None,
                                State.SocketSettings.SendTimeoutMs).ConfigureAwait(false);

                        if (State.FileTransferCanceled)
                        {
                            const string fileTransferStalledErrorMessage =
                                "Aborting file transfer, client says that data is no longer being received";

                            return Result.Fail(fileTransferStalledErrorMessage);
                        }

                        if (sendFileChunkResult.Failure)
                        {
                            return sendFileChunkResult;
                        }

                        State.LastBytesSentCount = sendFileChunkResult.Value;
                        numberOfBytesToSend -= State.LastBytesSentCount;
                        offset += State.LastBytesSentCount;
                        socketSendCount++;
                    }

                    fileChunkSentCount++;

                    if (State.OutgoingFileSize > (10 * State.BufferSize)) continue;
                    SocketEventOccurred?.Invoke(this,
                        new ServerEvent
                        {
                            EventType = EventType.SentFileChunkToClient,
                            FileSizeInBytes = State.OutgoingFileSize,
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
                        RemoteServerIpAddress = State.ClientSessionIpAddress,
                        RemoteServerPortNumber = State.ClientServerPort
                    });

                var receiveConfirationMessageResult =
                    await ReceiveConfirmationFileTransferCompleteAsync().ConfigureAwait(false);

                if (receiveConfirationMessageResult.Failure)
                {
                    return receiveConfirationMessageResult;
                }

                State.ServerSocket.Shutdown(SocketShutdown.Both);
                State.ServerSocket.Close();

                return Result.Ok();
            }
        }

        async Task<Result> ReceiveConfirmationFileTransferCompleteAsync()
        {
            var buffer = new byte[State.BufferSize];
            Result<int> receiveMessageResult;
            int bytesReceived;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceiveConfirmationMessageStarted,
                    RemoteServerIpAddress = State.ClientSessionIpAddress,
                    RemoteServerPortNumber = State.ClientServerPort
                });

            try
            {
                receiveMessageResult =
                    await State.ServerSocket.ReceiveAsync(
                        buffer,
                        0,
                        State.BufferSize,
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
                    RemoteServerIpAddress = State.ClientSessionIpAddress,
                    RemoteServerPortNumber = State.ClientServerPort,
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
            State.ClientInfo = newClient.ConnectionInfo;
            State.RemoteFilePath = remoteFilePath;
            State.ClientTransferFolderPath = Path.GetDirectoryName(remoteFilePath);
            State.MyTransferFolderPath = localFolderPath;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.RequestInboundFileTransferStarted,
                    RemoteServerIpAddress = State.ClientSessionIpAddress,
                    RemoteServerPortNumber = State.ClientServerPort,
                    RemoteFolder = State.ClientTransferFolderPath,
                    FileName = Path.GetFileName(State.RemoteFilePath),
                    LocalIpAddress = State.MyLocalIpAddress,
                    LocalPortNumber = State.MyServerPort,
                    LocalFolder = State.MyTransferFolderPath,
                });

            var connectResult =
                await ConnectToServerAsync();

            if (connectResult.Failure)
            {
                return connectResult;
            }

            var messageData =
                MessageWrapper.ConstructInboundFileTransferRequest(
                    State.RemoteFilePath,
                    State.MyLocalIpAddress,
                    State.MyServerPort,
                    State.MyTransferFolderPath);

            var sendMessageDataResult = await SendMessageData(messageData);
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

            var newClient = new RemoteServer(remoteServerIpAddress, remoteServerPort);
            State.ClientInfo = newClient.ConnectionInfo;
            State.IncomingFilePath = localFilePath;
            State.IncomingFileSize = fileSizeBytes;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceivedInboundFileTransferRequest,
                    LocalFolder = Path.GetDirectoryName(State.IncomingFilePath),
                    FileName = Path.GetFileName(State.IncomingFilePath),
                    FileSizeInBytes = State.IncomingFileSize,
                    RemoteServerIpAddress = State.ClientSessionIpAddress,
                    RemoteServerPortNumber = State.ClientServerPort
                });

            if (File.Exists(State.IncomingFilePath))
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
                    RemoteServerIpAddress = State.ClientSessionIpAddress,
                    RemoteServerPortNumber = State.ClientServerPort,
                    FileTransferStartTime = startTime,
                    FileSizeInBytes = State.IncomingFileSize
                });

            var receiveCount = 0;
            long totalBytesReceived = 0;
            var bytesRemaining = State.IncomingFileSize;
            float percentComplete = 0;
            State.FileTransferStalled = false;

            if (State.UnreadBytes.Count > 0)
            {
                totalBytesReceived += State.UnreadBytes.Count;
                bytesRemaining -= State.UnreadBytes.Count;

                var writeBytesResult =
                    FileHelper.WriteBytesToFile(
                        State.IncomingFilePath,
                        State.UnreadBytes.ToArray(),
                        State.UnreadBytes.Count);

                if (writeBytesResult.Failure)
                {
                    return writeBytesResult;
                }

                EventOccurred?.Invoke(this,
                    new ServerEvent
                    {
                        EventType = EventType.CopySavedBytesToIncomingFile,
                        CurrentFileBytesReceived = State.UnreadBytes.Count,
                        TotalFileBytesReceived = totalBytesReceived,
                        FileSizeInBytes = State.IncomingFileSize,
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

                State.LastBytesReceivedCount = readFromSocketResult.Value;
                var receivedBytes = new byte[State.LastBytesReceivedCount];

                if (State.LastBytesReceivedCount == 0)
                {
                    return Result.Fail("Socket is no longer receiving data, must abort file transfer");
                }

                var writeBytesResult =
                    FileHelper.WriteBytesToFile(
                        State.IncomingFilePath,
                        receivedBytes,
                        State.LastBytesReceivedCount);

                if (writeBytesResult.Failure)
                {
                    return writeBytesResult;
                }

                receiveCount++;
                totalBytesReceived += State.LastBytesReceivedCount;
                bytesRemaining -= State.LastBytesReceivedCount;
                var checkPercentComplete = totalBytesReceived / (float) State.IncomingFileSize;
                var changeSinceLastUpdate = checkPercentComplete - percentComplete;

                // this method fires on every socket read event, which could be hurdreds of thousands
                // of times or millions of times depending on the file size and buffer size. Since this 
                // event is only used by myself when debugging small test files, I limited this
                // event to only fire when the size of the file will result in less than 10 read events
                if (State.IncomingFileSize < (10 * State.BufferSize))
                {
                    SocketEventOccurred?.Invoke(this,
                        new ServerEvent
                        {
                            EventType = EventType.ReceivedFileBytesFromSocket,
                            SocketReadCount = receiveCount,
                            BytesReceived = State.LastBytesReceivedCount,
                            CurrentFileBytesReceived = State.LastBytesReceivedCount,
                            TotalFileBytesReceived = totalBytesReceived,
                            FileSizeInBytes = State.IncomingFileSize,
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
                            FileSizeInBytes = State.IncomingFileSize,
                            FileBytesRemaining = bytesRemaining,
                            PercentComplete = percentComplete
                        });
                }
            }

            if (State.FileTransferStalled)
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
                    FileSizeInBytes = State.IncomingFileSize,
                    RemoteServerIpAddress = State.ClientSessionIpAddress,
                    RemoteServerPortNumber = State.ClientServerPort
                });

            return Result.Ok();
        }

        public async Task<Result> SendNotificationFileTransferStalledAsync()
        {
            State.FileTransferStalled = true;

            await Task.Delay(1);

            return
                await SendSimpleMessageToClientAsync(
                    MessageType.FileTransferStalled,
                    EventType.SendFileTransferStalledStarted,
                    EventType.SendFileTransferStalledComplete);
        }

        Result HandleStalledFileTransfer(byte[] messageData)
        {
            (string requestorIpAddress,
                int requestorPortNumber) = MessageUnwrapper.ReadServerConnectionInfo(messageData);

            var newClient = new RemoteServer(requestorIpAddress, requestorPortNumber);
            State.ClientInfo = newClient.ConnectionInfo;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.FileTransferStalled,
                    RemoteServerIpAddress = State.ClientSessionIpAddress,
                    RemoteServerPortNumber = State.ClientServerPort
                });

            State.FileTransferCanceled = true;
            _signalSendNextFileChunk.Set();

            return Result.Ok();
        }

        public async Task<Result> RetryCanceledFileTransfer(
            string remoteServerIpAddress,
            int remoteServerPort)
        {
            var newClient = new RemoteServer(remoteServerIpAddress, remoteServerPort);
            State.ClientInfo = newClient.ConnectionInfo;

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
            State.ClientInfo = newClient.ConnectionInfo;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceivedRetryOutboundFileTransferRequest,
                    RemoteServerIpAddress = State.ClientSessionIpAddress,
                    RemoteServerPortNumber = State.ClientServerPort
                });

            await Task.Delay(1);

            return
                await SendFileAsync(
                    State.ClientSessionIpAddress,
                    State.ClientServerPort,
                    State.OutgoingFilePath,
                    State.ClientTransferFolderPath).ConfigureAwait(false);

            //if (_state.FileTransferCanceled)
            //{
            //    var cancelFileTransfer = await CancelStalledFileTransfer(
            //        requestorIpAddress,
            //        requestorPortNumber,
            //        _state.MyLocalIpAddress,
            //        _state.MyServerPort,
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
                    RemoteServerIpAddress = State.ClientSessionIpAddress,
                    RemoteServerPortNumber = State.ClientServerPort,
                    ConfirmationMessage = ConfirmationMessage
                });

            var confirmationMessageData = Encoding.ASCII.GetBytes(ConfirmationMessage);

            var sendConfirmatinMessageResult =
                await State.ClientSocket.SendWithTimeoutAsync(
                    confirmationMessageData,
                    0,
                    confirmationMessageData.Length,
                    SocketFlags.None,
                    State.SocketSettings.SendTimeoutMs).ConfigureAwait(false);

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
                    RemoteServerIpAddress = State.ClientSessionIpAddress,
                    RemoteServerPortNumber = State.ClientServerPort,
                    LocalIpAddress = State.MyLocalIpAddress,
                    LocalPortNumber = State.MyServerPort
                });

            var connectResult =
                await ConnectToServerAsync();

            if (connectResult.Failure)
            {
                return connectResult;
            }

            var messageData =
                MessageWrapper.ConstructGenericMessage(messageType, State.MyLocalIpAddress, State.MyServerPort);

            var sendMessageDataResult = await SendMessageData(messageData);
            if (sendMessageDataResult.Failure)
            {
                return sendMessageDataResult;
            }

            CloseServerSocket();

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = sendMessageCompleteEventType,
                    RemoteServerIpAddress = State.ClientSessionIpAddress,
                    RemoteServerPortNumber = State.ClientServerPort,
                    LocalIpAddress = State.MyLocalIpAddress,
                    LocalPortNumber = State.MyServerPort
                });

            return Result.Ok();
        }

        async Task<Result> ConnectToServerAsync()
        {
            State.ServerSocket =
                new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ConnectToRemoteServerStarted,
                    RemoteServerIpAddress = State.ClientSessionIpAddress,
                    RemoteServerPortNumber = State.ClientServerPort
                });

            var connectResult =
                await State.ServerSocket.ConnectWithTimeoutAsync(
                    State.ClientSessionIpAddress,
                    State.ClientServerPort,
                    State.SocketSettings.ConnectTimeoutMs).ConfigureAwait(false);

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
                await State.ServerSocket.SendWithTimeoutAsync(
                    messageLength,
                    0,
                    messageLength.Length,
                    SocketFlags.None,
                    State.SocketSettings.SendTimeoutMs).ConfigureAwait(false);

            if (sendMessageLengthResult.Failure)
            {
                return sendMessageLengthResult;
            }

            var sendMessageResult =
                await State.ServerSocket.SendWithTimeoutAsync(
                    messageData,
                    0,
                    messageData.Length,
                    SocketFlags.None,
                    State.SocketSettings.SendTimeoutMs).ConfigureAwait(false);

            return sendMessageResult.Success
                ? Result.Ok()
                : sendMessageResult;
        }

        void CloseServerSocket()
        {
            State.ServerSocket.Shutdown(SocketShutdown.Both);
            State.ServerSocket.Close();
        }

        public async Task<Result> RequestFileListAsync(
            string remoteServerIpAddress,
            int remoteServerPort,
            string targetFolder)
        {
            var newClient = new RemoteServer(remoteServerIpAddress, remoteServerPort);
            State.ClientInfo = newClient.ConnectionInfo;
            State.ClientTransferFolderPath = targetFolder;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.RequestFileListStarted,
                    LocalIpAddress = State.MyLocalIpAddress,
                    LocalPortNumber = State.MyServerPort,
                    RemoteServerIpAddress = State.ClientSessionIpAddress,
                    RemoteServerPortNumber = State.ClientServerPort,
                    RemoteFolder = State.ClientTransferFolderPath
                });

            var connectResult =
                await ConnectToServerAsync();

            if (connectResult.Failure)
            {
                return connectResult;
            }

            var messageData =
                MessageWrapper.ConstructFileListRequest(
                    State.MyLocalIpAddress,
                    State.MyServerPort,
                    State.ClientTransferFolderPath);

            var sendMessageDataResult = await SendMessageData(messageData);
            if (sendMessageDataResult.Failure)
            {
                return sendMessageDataResult;
            }

            CloseServerSocket();

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
            State.ClientInfo = newClient.ConnectionInfo;
            State.MyTransferFolderPath = targetFolderPath;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceivedFileListRequest,
                    RemoteServerIpAddress = State.ClientSessionIpAddress,
                    RemoteServerPortNumber = State.ClientServerPort,
                    RemoteFolder = State.MyTransferFolderPath
                });

            if (!Directory.Exists(State.MyTransferFolderPath))
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
                    Directory.GetFiles(State.MyTransferFolderPath).ToList()
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
                    LocalIpAddress = State.MyLocalIpAddress,
                    LocalPortNumber = State.MyServerPort,
                    RemoteServerIpAddress = State.ClientSessionIpAddress,
                    RemoteServerPortNumber = State.ClientServerPort,
                    FileInfoList = fileInfoList,
                    LocalFolder = State.MyTransferFolderPath,
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
                    State.MyLocalIpAddress,
                    State.MyServerPort,
                    State.MyTransferFolderPath);

            var sendMessageDataResult = await SendMessageData(responseData);
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

        Result HandleRequestedFolderDoesNotExist(byte[] messageData)
        {
            (string requestorIpAddress,
                int requestorPortNumber) = MessageUnwrapper.ReadServerConnectionInfo(messageData);

            var newClient = new RemoteServer(requestorIpAddress, requestorPortNumber);
            State.ClientInfo = newClient.ConnectionInfo;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceivedNotificationFolderDoesNotExist,
                    RemoteServerIpAddress = State.ClientSessionIpAddress,
                    RemoteServerPortNumber = State.ClientServerPort
                });

            return Result.Ok();
        }

        Result HandleNoFilesAvailableForDownload(byte[] messageData)
        {
            (string requestorIpAddress,
                int requestorPortNumber) = MessageUnwrapper.ReadServerConnectionInfo(messageData);

            var newClient = new RemoteServer(requestorIpAddress, requestorPortNumber);
            State.ClientInfo = newClient.ConnectionInfo;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceivedNotificationNoFilesToDownload,
                    RemoteServerIpAddress = State.ClientSessionIpAddress,
                    RemoteServerPortNumber = State.ClientServerPort
                });

            return Result.Ok();
        }

        Result ReceiveFileList(byte[] messageData)
        {
            var (remoteServerIp,
                remoteServerPort,
                transferFolder,
                fileInfoList) = MessageUnwrapper.ReadFileListResponse(messageData);

            var newClient = new RemoteServer(remoteServerIp, remoteServerPort);
            State.ClientInfo = newClient.ConnectionInfo;
            State.ClientTransferFolderPath = transferFolder;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceivedFileList,
                    LocalIpAddress = State.MyLocalIpAddress,
                    LocalPortNumber = State.MyServerPort,
                    RemoteServerIpAddress = State.ClientSessionIpAddress,
                    RemoteServerPortNumber = State.ClientServerPort,
                    RemoteFolder = State.ClientTransferFolderPath,
                    FileInfoList = fileInfoList
                });

            return Result.Ok();
        }

        public async Task<Result> RequestTransferFolderPathAsync(
            string remoteServerIpAddress,
            int remoteServerPort)
        {
            var newClient = new RemoteServer(remoteServerIpAddress, remoteServerPort);
            State.ClientInfo = newClient.ConnectionInfo;

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
            State.ClientInfo = newClient.ConnectionInfo;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceivedTransferFolderPathRequest,
                    RemoteServerIpAddress = State.ClientSessionIpAddress,
                    RemoteServerPortNumber = State.ClientServerPort
                });

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.SendTransferFolderPathStarted,
                    RemoteServerIpAddress = State.ClientSessionIpAddress,
                    RemoteServerPortNumber = State.ClientServerPort,
                    LocalFolder = State.MyTransferFolderPath
                });

            var connectResult =
                await ConnectToServerAsync();

            if (connectResult.Failure)
            {
                return connectResult;
            }

            var responseData =
                MessageWrapper.ConstructTransferFolderResponse(
                    State.MyLocalIpAddress,
                    State.MyServerPort,
                    State.MyTransferFolderPath);

            var sendMessageDataResult = await SendMessageData(responseData);
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

        Result ReceiveTransferFolderPath(byte[] messageData)
        {
            var (remoteServerIp,
                remoteServerPort,
                transferFolder) = MessageUnwrapper.ReadTransferFolderResponse(messageData);

            var newClient = new RemoteServer(remoteServerIp, remoteServerPort);
            State.ClientInfo = newClient.ConnectionInfo;
            State.ClientTransferFolderPath = transferFolder;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceivedTransferFolderPath,
                    RemoteServerIpAddress = State.ClientSessionIpAddress,
                    RemoteServerPortNumber = State.ClientServerPort,
                    RemoteFolder = State.ClientTransferFolderPath
                });

            return Result.Ok();
        }

        public async Task<Result> RequestPublicIpAsync(
            string remoteServerIpAddress,
            int remoteServerPort)
        {
            var newClient = new RemoteServer(remoteServerIpAddress, remoteServerPort);
            State.ClientInfo = newClient.ConnectionInfo;

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
            State.ClientInfo = newClient.ConnectionInfo;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceivedPublicIpAddressRequest,
                    RemoteServerIpAddress = State.ClientSessionIpAddress,
                    RemoteServerPortNumber = State.ClientServerPort
                });

            var publicIpResult = await Network.GetPublicIPv4AddressAsync().ConfigureAwait(false);
            if (publicIpResult.Failure)
            {
                return Result.Fail(publicIpResult.Error);
            }

            State.MyInfo.PublicIpAddress = publicIpResult.Value;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.SendPublicIpAddressStarted,
                    RemoteServerIpAddress = requestorIpAddress,
                    RemoteServerPortNumber = requestorPortNumber,
                    LocalIpAddress = State.MyLocalIpAddress,
                    LocalPortNumber = State.MyServerPort,
                    PublicIpAddress = State.MyPublicIpAddress
                });

            var connectResult =
                await ConnectToServerAsync();

            if (connectResult.Failure)
            {
                return connectResult;
            }

            var responseData =
                MessageWrapper.ConstructPublicIpAddressResponse(
                    State.MyLocalIpAddress,
                    State.MyServerPort,
                    State.MyPublicIpAddress);

            var sendMessageDataResult = await SendMessageData(responseData);
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

        Result ReceivePublicIpAddress(byte[] messageData)
        {
            var (remoteServerIp,
                remoteServerPort,
                publicIpAddress) = MessageUnwrapper.ReadPublicIpAddressResponse(messageData);

            var newClient = new RemoteServer(remoteServerIp, remoteServerPort);
            State.ClientInfo = newClient.ConnectionInfo;
            State.ClientInfo.PublicIpAddress = Network.ParseSingleIPv4Address(publicIpAddress).Value;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceivedPublicIpAddress,
                    RemoteServerIpAddress = State.ClientSessionIpAddress,
                    RemoteServerPortNumber = State.ClientServerPort,
                    PublicIpAddress = State.ClientPublicIpAddress
                });

            return Result.Ok();
        }

        public async Task<Result> ShutdownServerAsync()
        {
            if (ShutdownComplete)
            {
                return Result.Fail("Server is already shutdown");
            }

            var newClient = new RemoteServer(State.MyLocalIpAddress, State.MyServerPort);
            State.ClientInfo = newClient.ConnectionInfo;

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
            State.ClientInfo = newClient.ConnectionInfo;

            var ipComparison =
                Network.CompareTwoIpAddresses(State.LastAcceptedConnectionIp, State.MyInfo.LocalIpAddress);

            var ipsMatch = ipComparison == IpAddressSimilarity.AllBytesMatch;
            var portsMatch = State.ClientServerPort == State.MyServerPort;
            if (ipsMatch && portsMatch)
            {
                ReceivedShutdownCommand = true;
            }

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceivedShutdownServerCommand,
                    RemoteServerIpAddress = requestorIpAddress,
                    RemoteServerPortNumber = requestorPortNumber
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
                State.ListenSocket.Shutdown(SocketShutdown.Both);
                State.ListenSocket.Close();
            }
            catch (SocketException ex)
            {
                _log.Error("Error raised in method ShutdownListenSocket", ex);
                var errorMessage = $"{ex.Message} ({ex.GetType()} raised in method TplSocketServer.ShutdownListenSocket)";

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