namespace TplSocketServer
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
        const string FileTransferStalledErrorMessage = "Aborting file transfer, client says that data is no longer being received";

        ServerState _state;
        readonly AutoResetEvent _signalSendNextFileChunk = new AutoResetEvent(true);
        readonly AutoResetEvent _signalServerShutdown = new AutoResetEvent(false);
        readonly Logger _log = new Logger(typeof(TplSocketServer));
        
        public TplSocketServer(IPAddress localIpAddress, int port)
        {
            _state = new ServerState
            {
                LoggingEnabled = false,
                MyEndPoint = new IPEndPoint(localIpAddress, port),
                LocalFolder = new DirectoryInfo(GetDefaultTransferFolder()),
                SocketSettings = new SocketSettings
                {
                    MaxNumberOfConections = 5,
                    BufferSize = 1024,
                    ConnectTimeoutMs = 5000,
                    ReceiveTimeoutMs = 5000,
                    SendTimeoutMs = 5000
                },
                ListenSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)
            };
        }

        public bool LoggingEnabled
        {
            get => _state.LoggingEnabled;
            set => _state.LoggingEnabled = value;
        }

        public string TransferFolderPath
        {
            get => _state.LocalFolderPath;
            set => SetTransferFolderPath(value);
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
        
        public event EventHandler<ServerEvent> EventOccurred;
        public event EventHandler<ServerEvent> SocketEventOccurred;

        void SetTransferFolderPath(string folderPath)
        {
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            _state.LocalFolder = new DirectoryInfo(folderPath);
        }
        
        static string GetDefaultTransferFolder()
        {
            var defaultPath = $"{Directory.GetCurrentDirectory()}{Path.DirectorySeparatorChar}transfer";

            if (!Directory.Exists(defaultPath))
            {
                Directory.CreateDirectory(defaultPath);
            }

            return defaultPath;
        }
        
        public async Task<Result> HandleIncomingConnectionsAsync(CancellationToken token)
        {
            Logger.Start("server.log");

            var listenResult = Listen(string.Empty, _state.MyEndPoint.Port);
            if (listenResult.Failure)
            {
                return listenResult;
            }

            var runServerRersult = await WaitForConnectionsAsync(token);

            EventOccurred?.Invoke(this,
            new ServerEvent
                { EventType = EventType.ExitMainAcceptConnectionLoop });

            return runServerRersult;
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

            EventOccurred?.Invoke(this,
            new ServerEvent
            { EventType = EventType.ListenOnLocalPortStarted });

            var ipEndPoint = new IPEndPoint(ipToBind, localPort);
            try
            {
                _state.ListenSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _state.ListenSocket.Bind(ipEndPoint);
                _state.ListenSocket.Listen(_state.SocketSettings.MaxNumberOfConections);
            }
            catch (SocketException ex)
            {
                _log.Error("Error raised in method Listen", ex);
                return Result.Fail($"{ex.Message} ({ex.GetType()} raised in method TplSocketServer.Listen)");
            }

            EventOccurred?.Invoke(this,
            new ServerEvent
            { EventType = EventType.ListenOnLocalPortComplete });

            return Result.Ok();
        }

        async Task<Result> WaitForConnectionsAsync(CancellationToken token)
        {
            EventOccurred?.Invoke(this,
            new ServerEvent
                { EventType = EventType.EnterMainAcceptConnectionLoop });

            // Main loop. Server handles incoming connections until encountering an error
            while (true)
            {
                EventOccurred?.Invoke(this,
                new ServerEvent
                { EventType = EventType.AcceptConnectionAttemptStarted });

                var acceptResult = await _state.ListenSocket.AcceptTaskAsync().ConfigureAwait(false);

                if (token.IsCancellationRequested)
                {
                    token.ThrowIfCancellationRequested();
                }

                if (acceptResult.Failure)
                {
                    return acceptResult;
                }

                _state.ClientSocket = acceptResult.Value;
                _state.ClientEndPoint = _state.ClientSocket.RemoteEndPoint as IPEndPoint;

                if (_state.ClientEndPoint == null)
                {
                    return Result.Fail("Error occurred casting _state.ClientSocket.RemoteEndPoint as IPEndPoint");
                }

                EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.AcceptConnectionAttemptComplete,
                    RemoteServerIpAddress = _state.ClientEndPoint.Address.ToString(),
                    RemoteServerPortNumber = _state.ClientEndPoint.Port
                });

                var clientRequest = await HandleClientRequestAsync(token).ConfigureAwait(false);

                if (_state.ShutdownServer)
                {
                    _signalServerShutdown.Set();
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
            if (token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
            }

            var receiveMessageResult = await ReceiveMessageFromClientAsync(token).ConfigureAwait(false);
            if (receiveMessageResult.Failure)
            {
                return receiveMessageResult;
            }

            var messageData = receiveMessageResult.Value;
            var requestResult = await ProcessRequestAsync(messageData, token).ConfigureAwait(false);

            //if (_state.FileTransferCanceled)
            //{
            //    var cancelFileTransfer = await CancelStalledFileTransfer(
            //        _state.ClientEndPoint.Address.ToString(),
            //        _state.ClientEndPoint.Port,
            //        _state.LocalIpAddress,
            //        _state.LocalPort,
            //        token);

            //    _state.FileTransferCanceled = false;

            //    if (cancelFileTransfer.Failure) return cancelFileTransfer;
            //}

            try
            {
                _state.ClientSocket.Shutdown(SocketShutdown.Both);
                _state.ClientSocket.Close();
            }
            catch (SocketException ex)
            {
                _log.Error("Error raised in method CloseListenSocket", ex);
                return Result.Fail($"{ex.Message} ({ex.GetType()} raised in method TplSocketServer.CloseListenSocket)");
            }

            return requestResult;
        }

        async Task<Result<byte[]>> ReceiveMessageFromClientAsync(CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
            }

            EventOccurred?.Invoke(this,
            new ServerEvent
                { EventType = EventType.ReceiveMessageFromClientStarted });

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
                { EventType = EventType.ReceiveMessageFromClientComplete });

            return Result.Ok(messageData);
        }

        async Task<Result<int>> ReadFromSocketAsync()
        {
            Result<int> receiveResult;
            try
            {
                receiveResult =
                    await _state.ClientSocket.ReceiveWithTimeoutAsync(
                            _state.Buffer,
                            0,
                            _state.BufferSize,
                            0,
                            _state.SocketSettings.ReceiveTimeoutMs).ConfigureAwait(false);
            }
            catch (SocketException ex)
            {
                _log.Error("Error raised in method DetermineTransferTypeAsync", ex);
                return Result.Fail<int>($"{ex.Message} ({ex.GetType()} raised in method TplSocketServer.DetermineTransferTypeAsync)");
            }
            catch (TimeoutException ex)
            {
                _log.Error("Error raised in method DetermineTransferTypeAsync", ex);
                return Result.Fail<int>($"{ex.Message} ({ex.GetType()} raised in method TplSocketServer.DetermineTransferTypeAsync)");
            }

            return receiveResult;
        }

        async Task<Result<int>> DetermineMessageLengthAsync()
        {
            EventOccurred?.Invoke(this,
            new ServerEvent
                { EventType = EventType.DetermineMessageLengthStarted });

            var readFromSocketResult = await ReadFromSocketAsync().ConfigureAwait(false);
            if (readFromSocketResult.Failure)
            {
                return readFromSocketResult;
            }

            _state.LastBytesReceivedCount = readFromSocketResult.Value;
            var numberOfUnreadBytes = _state.LastBytesReceivedCount - 4;
            var messageLength = MessageUnwrapper.ReadInt32(_state.Buffer);

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
                MessageLengthInBytes = messageLength
            });

            return Result.Ok(messageLength);
        }

        async Task<Result<byte[]>> ReceiveAllMessageBytesAsync(int messageLength)
        {
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

            EventOccurred?.Invoke(this,
            new ServerEvent
                { EventType = EventType.ReceiveMessageBytesStarted });

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
                { EventType = EventType.ReceiveMessageBytesComplete });

            return Result.Ok(messageData.ToArray());
        }

        async Task<Result> ProcessRequestAsync(byte[] messageData, CancellationToken token)
        {
            EventOccurred?.Invoke(this,
            new ServerEvent
                { EventType = EventType.DetermineMessageTypeStarted });

            var transferTypeData = MessageUnwrapper.ReadInt32(messageData).ToString();
            var transferType = (MessageType)Enum.Parse(typeof(MessageType), transferTypeData);

            switch (transferType)
            {
                case MessageType.TextMessage:

                    EventOccurred?.Invoke(this,
                    new ServerEvent
                    {
                        EventType = EventType.DetermineMessageTypeComplete,
                        MessageType = MessageType.TextMessage
                    });

                    return ReceiveTextMessage(messageData, token);

                case MessageType.InboundFileTransfer:

                    EventOccurred?.Invoke(this,
                    new ServerEvent
                    {
                        EventType = EventType.DetermineMessageTypeComplete,
                        MessageType = MessageType.InboundFileTransfer
                    });

                    return await InboundFileTransferAsync(messageData, token).ConfigureAwait(false);

                case MessageType.OutboundFileTransfer:

                    EventOccurred?.Invoke(this,
                    new ServerEvent
                    {
                        EventType = EventType.DetermineMessageTypeComplete,
                        MessageType = MessageType.OutboundFileTransfer
                    });

                    return await OutboundFileTransferAsync(messageData, token).ConfigureAwait(false);

                case MessageType.FileTransferAccepted:

                    EventOccurred?.Invoke(this,
                        new ServerEvent
                        {
                            EventType = EventType.DetermineMessageTypeComplete,
                            MessageType = MessageType.FileTransferAccepted
                        });

                    return await HandleClientAcceptedFileTransferAsync(messageData, token);

                case MessageType.FileTransferStalled:

                    EventOccurred?.Invoke(this,
                    new ServerEvent
                    {
                        EventType = EventType.DetermineMessageTypeComplete,
                        MessageType = MessageType.FileTransferStalled
                    });

                    return HandleStalledFileTransfer(messageData);

                case MessageType.FileListRequest:

                    EventOccurred?.Invoke(this,
                    new ServerEvent
                    {
                        EventType = EventType.DetermineMessageTypeComplete,
                        MessageType = MessageType.FileListRequest
                    });

                    return await SendFileList(messageData, token).ConfigureAwait(false);

                case MessageType.FileListResponse:

                    EventOccurred?.Invoke(this,
                    new ServerEvent
                    {
                        EventType = EventType.DetermineMessageTypeComplete,
                        MessageType = MessageType.FileListResponse
                    });

                    return ReceiveFileList(messageData, token);

                case MessageType.TransferFolderPathRequest:

                    EventOccurred?.Invoke(this,
                    new ServerEvent
                    {
                        EventType = EventType.DetermineMessageTypeComplete,
                        MessageType = MessageType.TransferFolderPathRequest
                    });

                    return await SendTransferFolderResponseAsync(messageData, token).ConfigureAwait(false);

                case MessageType.TransferFolderPathResponse:

                    EventOccurred?.Invoke(this,
                    new ServerEvent
                    {
                        EventType = EventType.DetermineMessageTypeComplete,
                        MessageType = MessageType.TransferFolderPathResponse
                    });

                    return ReceiveTransferFolderResponse(messageData, token);

                case MessageType.PublicIpAddressRequest:

                    EventOccurred?.Invoke(this,
                    new ServerEvent
                    {
                        EventType = EventType.DetermineMessageTypeComplete,
                        MessageType = MessageType.PublicIpAddressRequest
                    });

                    return await SendPublicIpAddress(messageData, token).ConfigureAwait(false);

                case MessageType.PublicIpAddressResponse:

                    EventOccurred?.Invoke(this,
                    new ServerEvent
                    {
                        EventType = EventType.DetermineMessageTypeComplete,
                        MessageType = MessageType.PublicIpAddressResponse
                    });

                    return ReceivePublicIpAddress(messageData, token);

                case MessageType.NoFilesAvailableForDownload:

                    EventOccurred?.Invoke(this,
                    new ServerEvent
                    {
                        EventType = EventType.DetermineMessageTypeComplete,
                        MessageType = MessageType.NoFilesAvailableForDownload
                    });

                    return HandleNoFilesAvailableForDownload(messageData);

                case MessageType.FileTransferRejected:

                    EventOccurred?.Invoke(this,
                    new ServerEvent
                    {
                        EventType = EventType.DetermineMessageTypeComplete,
                        MessageType = MessageType.FileTransferRejected
                    });

                    return HandleRejectedFileTransfer(messageData);

                case MessageType.FileTransferCanceled:

                    EventOccurred?.Invoke(this,
                    new ServerEvent
                    {
                        EventType = EventType.DetermineMessageTypeComplete,
                        MessageType = MessageType.FileTransferCanceled
                    });

                    return HandleCanceledFileTransfer(messageData);

                case MessageType.RetryOutboundFileTransfer:

                    EventOccurred?.Invoke(this,
                        new ServerEvent
                        {
                            EventType = EventType.DetermineMessageTypeComplete,
                            MessageType = MessageType.RetryOutboundFileTransfer
                        });

                    return await RetryOutboundFileTransferAsync(messageData, token);

                case MessageType.RequestedFolderDoesNotExist:

                    EventOccurred?.Invoke(this,
                    new ServerEvent
                    {
                        EventType = EventType.DetermineMessageTypeComplete,
                        MessageType = MessageType.RequestedFolderDoesNotExist
                    });

                    return HandleRequestedFolderDoesNotExist(messageData);

                case MessageType.ShutdownServer:

                    EventOccurred?.Invoke(this,
                        new ServerEvent
                        {
                            EventType = EventType.DetermineMessageTypeComplete,
                            MessageType = MessageType.ShutdownServer
                        });

                    return HandleShutdownServerCommand(messageData);

                default:

                    var error = $"Unable to determine transfer type, value of '{transferType}' is invalid.";
                    return Result.Fail(error);
            }
        }
        
        Result ReceiveTextMessage(byte[] messageData, CancellationToken token)
        {
            EventOccurred?.Invoke(this,
                new ServerEvent
                    { EventType = EventType.ReadTextMessageStarted });

            var (message,
                remoteIpAddress,
                remotePortNumber) = MessageUnwrapper.ReadTextMessage(messageData);

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReadTextMessageComplete,
                    TextMessage = message,
                    RemoteServerIpAddress = remoteIpAddress,
                    RemoteServerPortNumber = remotePortNumber
                });

            return Result.Ok();
        }

        async Task<Result> InboundFileTransferAsync(byte[] messageData, CancellationToken token)
        {
            EventOccurred?.Invoke(this,
                new ServerEvent
                    { EventType = EventType.ReadInboundFileTransferInfoStarted });

            var (localFilePath,
                fileSizeBytes,
                remoteIpAddress,
                remotePort) = MessageUnwrapper.ReadInboundFileTransferRequest(messageData);

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReadInboundFileTransferInfoComplete,
                    LocalFolder = Path.GetDirectoryName(localFilePath),
                    FileName = Path.GetFileName(localFilePath),
                    FileSizeInBytes = fileSizeBytes,
                    RemoteServerIpAddress = remoteIpAddress,
                    RemoteServerPortNumber = remotePort
                });

            if (File.Exists(localFilePath))
            {
                return
                    await SendSimpleMessageToClientAsync(
                        MessageType.FileTransferRejected,
                        EventType.SendFileTransferRejectedStarted,
                        EventType.SendFileTransferRejectedComplete,
                        remoteIpAddress,
                        remotePort,
                        _state.LocalIpAddress,
                        _state.LocalPort,
                        token);
            }

            var acceptTransferResult =
                await SendSimpleMessageToClientAsync(
                    MessageType.FileTransferAccepted,
                    EventType.SendFileTransferAcceptedStarted,
                    EventType.SendFileTransferAcceptedComplete,
                    remoteIpAddress,
                    remotePort,
                    _state.LocalIpAddress,
                    _state.LocalPort,
                    token);

            if (acceptTransferResult.Failure)
            {
                return acceptTransferResult;
            }

            var receiveFileResult =
                await ReceiveFileAsync(
                    remoteIpAddress,
                    remotePort,
                    localFilePath,
                    fileSizeBytes,
                    token).ConfigureAwait(false);

            if (receiveFileResult.Failure)
            {
                return receiveFileResult;
            }

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.SendConfirmationMessageStarted,
                    ConfirmationMessage = ConfirmationMessage
                });

            var confirmationMessageData = Encoding.ASCII.GetBytes(ConfirmationMessage);

            var sendConfirmatinMessageResult =
                await _state.ClientSocket.SendWithTimeoutAsync(
                    confirmationMessageData,
                    0,
                    confirmationMessageData.Length,
                    0,
                    _state.SocketSettings.SendTimeoutMs).ConfigureAwait(false);

            if (sendConfirmatinMessageResult.Failure)
            {
                return sendConfirmatinMessageResult;
            }

            EventOccurred?.Invoke(this,
                new ServerEvent
                    { EventType = EventType.SendConfirmationMessageComplete });

            return Result.Ok();
        }

        async Task<Result> ReceiveFileAsync(
            string remoteIpAddress,
            int remotePort,
            string localFilePath,
            long fileSizeInBytes,
            CancellationToken token)
        {
            var receiveCount = 0;
            long totalBytesReceived = 0;
            var bytesRemaining = fileSizeInBytes;
            float percentComplete = 0;
            _state.FileTransferStalled = false;

            if (_state.UnreadBytes.Count > 0)
            {
                totalBytesReceived += _state.UnreadBytes.Count;
                bytesRemaining -= _state.UnreadBytes.Count;

                var writeBytesResult =
                    FileHelper.WriteBytesToFile(
                        localFilePath,
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
                        FileSizeInBytes = fileSizeInBytes,
                        FileBytesRemaining = bytesRemaining
                    });
            }

            var startTime = DateTime.Now;
            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceiveFileBytesStarted,
                    FileTransferStartTime = startTime,
                    FileSizeInBytes = fileSizeInBytes
                });

            // Read file bytes from transfer socket until 
            //      1. the entire file has been received OR 
            //      2. Data is no longer being received OR
            //      3, Transfer is canceled
            while (bytesRemaining > 0)
            {
                if (token.IsCancellationRequested)
                {
                    token.ThrowIfCancellationRequested();
                }

                var readFromSocketResult = await ReadFromSocketAsync().ConfigureAwait(false);
                if (readFromSocketResult.Failure)
                {
                    return Result.Fail<byte[]>(readFromSocketResult.Error);
                }

                _state.LastBytesReceivedCount = readFromSocketResult.Value;
                var receivedBytes = new byte[_state.LastBytesReceivedCount];

                if (_state.LastBytesReceivedCount == 0)
                {
                    return Result.Fail("Socket is no longer receiving data, must abort file transfer");
                }

                var writeBytesResult =
                    FileHelper.WriteBytesToFile(localFilePath, receivedBytes, _state.LastBytesReceivedCount);

                if (writeBytesResult.Failure)
                {
                    return writeBytesResult;
                }

                receiveCount++;
                totalBytesReceived += _state.LastBytesReceivedCount;
                bytesRemaining -= _state.LastBytesReceivedCount;
                var checkPercentComplete = totalBytesReceived / (float)fileSizeInBytes;
                var changeSinceLastUpdate = checkPercentComplete - percentComplete;

                // this method fires on every socket read event, which could be hurdreds of thousands
                // of times or millions of times depending on the file size and messageData size. Since this 
                // event is only used by myself when debugging small test files, I limited this
                // event to only fire when the size of the file will result in less than 15 read events
                if (fileSizeInBytes < (15 * _state.BufferSize))
                {
                    SocketEventOccurred?.Invoke(this,
                        new ServerEvent
                        {
                            EventType = EventType.ReceivedFileBytesFromSocket,
                            SocketReadCount = receiveCount,
                            BytesReceived = _state.LastBytesReceivedCount,
                            CurrentFileBytesReceived = _state.LastBytesReceivedCount,
                            TotalFileBytesReceived = totalBytesReceived,
                            FileSizeInBytes = fileSizeInBytes,
                            FileBytesRemaining = bytesRemaining,
                            PercentComplete = percentComplete
                        });
                }
                
                // Report progress only if at least 1% of file has been received since the last update
                if (changeSinceLastUpdate > TransferUpdateInterval)
                {
                    percentComplete = checkPercentComplete;
                    EventOccurred?.Invoke(this,
                        new ServerEvent
                        {
                            EventType = EventType.UpdateFileTransferProgress,
                            TotalFileBytesReceived = totalBytesReceived,
                            FileSizeInBytes = fileSizeInBytes,
                            FileBytesRemaining = bytesRemaining,
                            PercentComplete = percentComplete
                        });
                }
            }

            if (_state.FileTransferStalled)
            {
                return Result.Fail("Data is no longer bring received from remote client, file transfer has been canceled");
            }

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceiveFileBytesComplete,
                    FileTransferStartTime = startTime,
                    FileTransferCompleteTime = DateTime.Now,
                    FileSizeInBytes = fileSizeInBytes,
                    RemoteServerIpAddress = remoteIpAddress,
                    RemoteServerPortNumber = remotePort
                });

            return Result.Ok();
        }

        Result HandleRejectedFileTransfer(byte[] messageData)
        {
            EventOccurred?.Invoke(this,
                new ServerEvent
                    { EventType = EventType.ReceiveFileTransferRejectedStarted });

            (string requestorIpAddress,
                int requestorPortNumber) = MessageUnwrapper.ReadServerConnectionInfo(messageData);

            _state.OutgoingFile = null;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceiveFileTransferRejectedComplete,
                    RemoteServerIpAddress = requestorIpAddress,
                    RemoteServerPortNumber = requestorPortNumber
                });
            
            return Result.Ok();
        }

        async Task<Result> HandleClientAcceptedFileTransferAsync(byte[] messageData, CancellationToken token)
        {
            EventOccurred?.Invoke(this,
                new ServerEvent
                    { EventType = EventType.ReceiveFileTransferAcceptedStarted });

            (string requestorIpAddress,
                int requestorPortNumber) = MessageUnwrapper.ReadServerConnectionInfo(messageData);

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceiveFileTransferAcceptedComplete,
                    RemoteServerIpAddress = requestorIpAddress,
                    RemoteServerPortNumber = requestorPortNumber
                });
            
            return await SendFileBytesAsync(_state.OutgoingFilePath, token);

            //if (_state.FileTransferCanceled)
            //{
            //    var cancelFileTransfer = 
            //        await CancelStalledFileTransfer(
            //            requestorIpAddress,
            //            requestorPortNumber,
            //            _state.LocalIpAddress,
            //            _state.LocalPort,
            //            token);

            //    _state.FileTransferCanceled = false;
            //    if (cancelFileTransfer.Failure) return cancelFileTransfer;
            //}
        }

        public async Task<Result> SendNotificationFileTransferStalledAsync(
            string remoteServerIpAddress,
            int remoteServerPort,
            string localIpAddress,
            int localPort,
            CancellationToken token)
        {
            _state.FileTransferStalled = true;

            return
                await SendSimpleMessageToClientAsync(
                    MessageType.FileTransferStalled,
                    EventType.SendFileTransferStalledStarted,
                    EventType.SendFileTransferStalledComplete,
                    remoteServerIpAddress,
                    remoteServerPort,
                    localIpAddress,
                    localPort,
                    token);
        }

        Result HandleStalledFileTransfer(byte[] messageData)
        {
            EventOccurred?.Invoke(this,
                new ServerEvent
                    { EventType = EventType.ReceiveFileTransferStalledStarted });

            (string requestorIpAddress,
                int requestorPortNumber) = MessageUnwrapper.ReadServerConnectionInfo(messageData);

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceiveFileTransferStalledComplete,
                    RemoteServerIpAddress = requestorIpAddress,
                    RemoteServerPortNumber = requestorPortNumber
                });

            _state.FileTransferCanceled = true;
            _signalSendNextFileChunk.Set();

            return Result.Ok();
        }

        public async Task<Result> CancelStalledFileTransfer(
            string remoteServerIpAddress,
            int remoteServerPort,
            string localIpAddress,
            int localPort,
            CancellationToken token)
        {
            return
                await SendSimpleMessageToClientAsync(
                    MessageType.FileTransferCanceled,
                    EventType.SendFileTransferCanceledStarted,
                    EventType.SendFileTransferCanceledComplete,
                    remoteServerIpAddress,
                    remoteServerPort,
                    localIpAddress,
                    localPort,
                    token);
        }

        Result HandleCanceledFileTransfer(byte[] messageData)
        {
            EventOccurred?.Invoke(this,
                new ServerEvent
                    { EventType = EventType.ReceiveFileTransferCanceledStarted });

            (string requestorIpAddress,
                int requestorPortNumber) = MessageUnwrapper.ReadServerConnectionInfo(messageData);

            _state.FileTransferStalled = false;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceiveFileTransferCanceledComplete,
                    RemoteServerIpAddress = requestorIpAddress,
                    RemoteServerPortNumber = requestorPortNumber
                });

            return Result.Ok();
        }

        async Task<Result> RetryOutboundFileTransferAsync(byte[] messageData, CancellationToken token)
        {
            EventOccurred?.Invoke(this,
                new ServerEvent
                    { EventType = EventType.ReceiveRetryOutboundFileTransferStarted });

            (string requestorIpAddress,
                int requestorPortNumber) = MessageUnwrapper.ReadServerConnectionInfo(messageData);

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceiveRetryOutboundFileTransferComplete,
                    RemoteServerIpAddress = requestorIpAddress,
                    RemoteServerPortNumber = requestorPortNumber
                });

            return
                await SendFileAsync(
                    requestorIpAddress,
                    requestorPortNumber,
                    _state.OutgoingFilePath,
                    _state.RemoteFolderPath,
                    token).ConfigureAwait(false);

            //if (_state.FileTransferCanceled)
            //{
            //    var cancelFileTransfer = await CancelStalledFileTransfer(
            //        requestorIpAddress,
            //        requestorPortNumber,
            //        _state.LocalIpAddress,
            //        _state.LocalPort,
            //        token);

            //    _state.FileTransferCanceled = false;

            //    if (cancelFileTransfer.Failure) return cancelFileTransfer;
            //}            
        }

        async Task<Result> OutboundFileTransferAsync(byte[] messageData, CancellationToken token)
        {
            EventOccurred?.Invoke(this,
                new ServerEvent
                    { EventType = EventType.ReadOutboundFileTransferInfoStarted });

            var (requestedFilePath,
                remoteServerIpAddress,
                remoteServerPort,
                remoteFolderPath) = MessageUnwrapper.ReadOutboundFileTransferRequest(messageData);

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReadOutboundFileTransferInfoComplete,
                    LocalFolder = Path.GetDirectoryName(requestedFilePath),
                    FileName = Path.GetFileName(requestedFilePath),
                    FileSizeInBytes = new FileInfo(requestedFilePath).Length,
                    RemoteFolder = remoteFolderPath,
                    RemoteServerIpAddress = remoteServerIpAddress,
                    RemoteServerPortNumber = remoteServerPort
                });

            if (!File.Exists(requestedFilePath))
            {
                //TODO: Add another event sequence here
                return Result.Fail("File does not exist: " + requestedFilePath);
            }

            return
                await SendFileAsync(
                    remoteServerIpAddress,
                    remoteServerPort,
                    requestedFilePath,
                    remoteFolderPath,
                    token).ConfigureAwait(false);

            //if (_state.FileTransferCanceled)
            //{
            //    var cancelFileTransfer = await CancelStalledFileTransfer(
            //        remoteServerIpAddress,
            //        remoteServerPort,
            //        _state.LocalIpAddress,
            //        _state.LocalPort,
            //        token);

            //    _state.FileTransferCanceled = false;

            //    if (cancelFileTransfer.Failure) return cancelFileTransfer;
            //}            
        }

        public async Task<Result> SendFileAsync(
            string remoteServerIpAddress,
            int remoteServerPort,
            string localFilePath,
            string remoteFolderPath,
            CancellationToken token)
        {
            _state.OutgoingFile = new FileInfo(localFilePath);
            _state.RemoteFolder = new DirectoryInfo(remoteFolderPath);

            return await SendOutboundFileTransferRequestAsync(
                remoteServerIpAddress,
                remoteServerPort,
                localFilePath,
                remoteFolderPath,
                token);
        }

        public async Task<Result> RetryCanceledFileTransfer(
            string remoteServerIpAddress,
            int remoteServerPort,
            CancellationToken token)
        {
            return await SendSimpleMessageToClientAsync(
                MessageType.RetryOutboundFileTransfer,
                EventType.SendRetryOutboundFileTransferStarted,
                EventType.SendRetryOutboundFileTransferComplete,
                remoteServerIpAddress,
                remoteServerPort,
                _state.LocalIpAddress,
                _state.LocalPort,
                token);
        }

        async Task<Result> SendFileList(byte[] messageData, CancellationToken token)
        {
            EventOccurred?.Invoke(this,
            new ServerEvent
            { EventType = EventType.ReadFileListRequestStarted });

            (string requestorIpAddress,
                int requestorPortNumber,
                string targetFolderPath) = MessageUnwrapper.ReadFileListRequest(messageData);

            EventOccurred?.Invoke(this,
            new ServerEvent
            {
                EventType = EventType.ReadFileListRequestComplete,
                RemoteServerIpAddress = requestorIpAddress,
                RemoteServerPortNumber = requestorPortNumber,
                RemoteFolder = targetFolderPath
            });

            if (!Directory.Exists(targetFolderPath))
            {
                return
                    await SendSimpleMessageToClientAsync(
                        MessageType.RequestedFolderDoesNotExist,
                        EventType.SendNotificationFolderDoesNotExistStarted,
                        EventType.SendNotificationFolderDoesNotExistComplete,
                        requestorIpAddress,
                        requestorPortNumber,
                        _state.LocalIpAddress,
                        _state.LocalPort,
                        token);
            }

            List<string> listOfFiles;
            try
            {
                listOfFiles = Directory.GetFiles(targetFolderPath).ToList();
            }
            catch (IOException ex)
            {
                _log.Error("Error raised in method SendFileList", ex);
                return Result.Fail($"{ex.Message} ({ex.GetType()} raised in method TplSocketServer.SendFileList)");
            }

            if (listOfFiles.Count == 0)
            {
                return await SendSimpleMessageToClientAsync(
                    MessageType.NoFilesAvailableForDownload,
                    EventType.SendNotificationNoFilesToDownloadStarted,
                    EventType.SendNotificationNoFilesToDownloadComplete,
                    requestorIpAddress,
                    requestorPortNumber,
                    _state.LocalIpAddress,
                    _state.LocalPort,
                    token);
            }

            var fileInfoList = new List<(string, long)>();
            foreach (var file in listOfFiles)
            {
                var fileSize = new FileInfo(file).Length;
                fileInfoList.Add((file, fileSize));
            }

            var connectResult =
                await ConnectToServerAsync(requestorIpAddress, requestorPortNumber, token);

            if (connectResult.Failure)
            {
                return connectResult;
            }

            EventOccurred?.Invoke(this,
            new ServerEvent
            {
                EventType = EventType.SendFileListResponseStarted,
                LocalIpAddress = _state.LocalIpAddress,
                LocalPortNumber = _state.LocalPort,
                RemoteServerIpAddress = requestorIpAddress,
                RemoteServerPortNumber = requestorPortNumber,
                FileInfoList = fileInfoList,
                LocalFolder = targetFolderPath,
            });

            var responseData =
                MessageWrapper.ConstructFileListResponse(
                    fileInfoList,
                    "*",
                    "|",
                    _state.LocalIpAddress,
                    _state.LocalPort,
                    requestorIpAddress,
                    requestorPortNumber,
                    targetFolderPath);

            var sendMessageDataResult = await SendMessageData(responseData);
            if (sendMessageDataResult.Failure)
            {
                return sendMessageDataResult;
            }

            EventOccurred?.Invoke(this,
            new ServerEvent
            { EventType = EventType.SendFileListResponseComplete });

            CloseServerSocket();

            return Result.Ok();
        }

        Result HandleRequestedFolderDoesNotExist(byte[] messageData)
        {
            EventOccurred?.Invoke(this,
                new ServerEvent
                    { EventType = EventType.ReceiveNotificationFolderDoesNotExistStarted });

            (string requestorIpAddress,
                int requestorPortNumber) = MessageUnwrapper.ReadServerConnectionInfo(messageData);

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceiveNotificationFolderDoesNotExistComplete,
                    RemoteServerIpAddress = requestorIpAddress,
                    RemoteServerPortNumber = requestorPortNumber
                });

            return Result.Ok();
        }

        Result HandleNoFilesAvailableForDownload(byte[] messageData)
        {
            EventOccurred?.Invoke(this,
                new ServerEvent
                    { EventType = EventType.ReceiveNotificationNoFilesToDownloadStarted });

            (string requestorIpAddress,
                int requestorPortNumber) = MessageUnwrapper.ReadServerConnectionInfo(messageData);

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceiveNotificationNoFilesToDownloadComplete,
                    RemoteServerIpAddress = requestorIpAddress,
                    RemoteServerPortNumber = requestorPortNumber
                });

            return Result.Ok();
        }

        Result ReceiveFileList(byte[] messageData, CancellationToken token)
        {
            EventOccurred?.Invoke(this,
            new ServerEvent
            { EventType = EventType.ReadFileListResponseStarted });

            var (remoteServerIp,
                remoteServerPort,
                localIp,
                localPort,
                transferFolder,
                fileInfoList) = MessageUnwrapper.ReadFileListResponse(messageData);

            if (token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
            }

            EventOccurred?.Invoke(this,
            new ServerEvent
            {
                EventType = EventType.ReadFileListResponseComplete,
                RemoteServerIpAddress = remoteServerIp,
                RemoteServerPortNumber = remoteServerPort,
                LocalIpAddress = localIp,
                LocalPortNumber = localPort,
                RemoteFolder = transferFolder,
                FileInfoList = fileInfoList
            });

            return Result.Ok();
        }

        async Task<Result> SendTransferFolderResponseAsync(byte[] messageData, CancellationToken token)
        {
            EventOccurred?.Invoke(this,
            new ServerEvent
            { EventType = EventType.ReadTransferFolderRequestStarted });

            (string requestorIpAddress,
                int requestorPortNumber) = MessageUnwrapper.ReadServerConnectionInfo(messageData);

            EventOccurred?.Invoke(this,
            new ServerEvent
            {
                EventType = EventType.ReadTransferFolderRequestComplete,
                RemoteServerIpAddress = requestorIpAddress,
                RemoteServerPortNumber = requestorPortNumber
            });

            var connectResult =
                await ConnectToServerAsync(requestorIpAddress, requestorPortNumber, token);

            if (connectResult.Failure)
            {
                return connectResult;
            }

            EventOccurred?.Invoke(this,
            new ServerEvent
            {
                EventType = EventType.SendTransferFolderResponseStarted,
                RemoteServerIpAddress = requestorIpAddress,
                RemoteServerPortNumber = requestorPortNumber,
                LocalFolder = TransferFolderPath
            });

            var responseData =
                MessageWrapper.ConstructTransferFolderResponse(
                    _state.LocalIpAddress,
                    _state.LocalPort,
                    TransferFolderPath);

            var sendMessageDataResult = await SendMessageData(responseData);
            if (sendMessageDataResult.Failure)
            {
                return sendMessageDataResult;
            }

            EventOccurred?.Invoke(this,
            new ServerEvent
            { EventType = EventType.SendTransferFolderRequestComplete });

            CloseServerSocket();

            return Result.Ok();
        }

        Result ReceiveTransferFolderResponse(byte[] messageData, CancellationToken token)
        {
            EventOccurred?.Invoke(this,
            new ServerEvent
            { EventType = EventType.ReadTransferFolderResponseStarted });

            var (remoteServerIp,
                remoteServerPort,
                transferFolder) = MessageUnwrapper.ReadTransferFolderResponse(messageData);

            if (token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
            }

            EventOccurred?.Invoke(this,
            new ServerEvent
            {
                EventType = EventType.ReadTransferFolderResponseComplete,
                RemoteServerIpAddress = remoteServerIp,
                RemoteServerPortNumber = remoteServerPort,
                RemoteFolder = transferFolder
            });

            return Result.Ok();
        }

        async Task<Result> SendPublicIpAddress(byte[] messageData, CancellationToken token)
        {
            EventOccurred?.Invoke(this,
            new ServerEvent
            { EventType = EventType.ReadTransferFolderRequestStarted });

            (string requestorIpAddress,
                int requestorPortNumber) = MessageUnwrapper.ReadServerConnectionInfo(messageData);

            EventOccurred?.Invoke(this,
            new ServerEvent
            {
                EventType = EventType.ReadPublicIpRequestComplete,
                RemoteServerIpAddress = requestorIpAddress,
                RemoteServerPortNumber = requestorPortNumber
            });

            var connectResult =
                await ConnectToServerAsync(requestorIpAddress, requestorPortNumber, token);

            if (connectResult.Failure)
            {
                return connectResult;
            }

            var publicIp = IPAddress.None.ToString();
            var publicIpResult = await Network.GetPublicIPv4AddressAsync().ConfigureAwait(false);
            if (publicIpResult.Success)
            {
                publicIp = publicIpResult.Value.ToString();
            }

            EventOccurred?.Invoke(this,
            new ServerEvent
            {
                EventType = EventType.SendPublicIpResponseStarted,
                RemoteServerIpAddress = requestorIpAddress,
                RemoteServerPortNumber = requestorPortNumber,
                LocalIpAddress = _state.LocalIpAddress,
                LocalPortNumber = _state.LocalPort,
                PublicIpAddress = publicIp
            });

            var responseData =
                MessageWrapper.ConstructPublicIpAddressResponse(
                    _state.LocalIpAddress,
                    _state.LocalPort,
                    publicIp);

            var sendMessageDataResult = await SendMessageData(responseData);
            if (sendMessageDataResult.Failure)
            {
                return sendMessageDataResult;
            }

            EventOccurred?.Invoke(this,
            new ServerEvent
            { EventType = EventType.SendPublicIpResponseComplete });

            CloseServerSocket();

            return Result.Ok();
        }

        Result ReceivePublicIpAddress(byte[] messageData, CancellationToken token)
        {
            EventOccurred?.Invoke(this,
            new ServerEvent
            { EventType = EventType.ReadPublicIpResponseStarted });

            var (remoteServerIp,
                remoteServerPort,
                publicIpAddress) = MessageUnwrapper.ReadPublicIpAddressResponse(messageData);

            if (token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
            }

            EventOccurred?.Invoke(this,
            new ServerEvent
            {
                EventType = EventType.ReadPublicIpResponseComplete,
                RemoteServerIpAddress = remoteServerIp,
                RemoteServerPortNumber = remoteServerPort,
                PublicIpAddress = publicIpAddress
            });

            return Result.Ok();
        }

        async Task<Result> SendSimpleMessageToClientAsync(
            MessageType messageType,
            EventType sendMessageStartedEvent,
            EventType sendMessageCompleteEvent,
            string remoteServerIpAddress,
            int remoteServerPort,
            string localIpAddress,
            int localPort,
            CancellationToken token)
        {
            var connectResult =
                await ConnectToServerAsync(remoteServerIpAddress, remoteServerPort, token);

            if (connectResult.Failure)
            {
                return connectResult;
            }

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = sendMessageStartedEvent,
                    RemoteServerIpAddress = remoteServerIpAddress,
                    RemoteServerPortNumber = remoteServerPort,
                    LocalIpAddress = localIpAddress,
                    LocalPortNumber = localPort
                });

            var messageData =
                MessageWrapper.ConstructGenericMessage(messageType, localIpAddress, localPort);

            var sendMessageDataResult = await SendMessageData(messageData);
            if (sendMessageDataResult.Failure)
            {
                return sendMessageDataResult;
            }

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = sendMessageCompleteEvent,
                    RemoteServerIpAddress = remoteServerIpAddress,
                    RemoteServerPortNumber = remoteServerPort,
                    LocalIpAddress = localIpAddress,
                    LocalPortNumber = localPort
                });

            CloseServerSocket();

            return Result.Ok();
        }

        async Task<Result> ConnectToServerAsync(
            string remoteServerIpAddress,
            int remoteServerPort,
            CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
            }

            _state.ServerSocket =
                new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            EventOccurred?.Invoke(this,
            new ServerEvent
                { EventType = EventType.ConnectToRemoteServerStarted });

            var connectResult =
                await _state.ServerSocket.ConnectWithTimeoutAsync(
                    remoteServerIpAddress,
                    remoteServerPort,
                    _state.SocketSettings.ConnectTimeoutMs).ConfigureAwait(false);

            if (connectResult.Failure)
            {
                return connectResult;
            }

            EventOccurred?.Invoke(this,
            new ServerEvent
                { EventType = EventType.ConnectToRemoteServerComplete });

            return Result.Ok();
        }

        async Task<Result> SendMessageData(byte[] messageData)
        {
            var messageLength = BitConverter.GetBytes(messageData.Length);

            var sendMessageLengthResult =
                await _state.ServerSocket.SendWithTimeoutAsync(
                    messageLength,
                    0,
                    messageLength.Length,
                    0,
                    _state.SocketSettings.SendTimeoutMs).ConfigureAwait(false);

            if (sendMessageLengthResult.Failure)
            {
                return sendMessageLengthResult;
            }

            var sendMessageResult =
                await _state.ServerSocket.SendWithTimeoutAsync(
                    messageData,
                    0,
                    messageData.Length,
                    0,
                    _state.SocketSettings.SendTimeoutMs).ConfigureAwait(false);

            return sendMessageResult.Success
                ? Result.Ok()
                : sendMessageResult;
        }

        void CloseServerSocket()
        {
            _state.ServerSocket.Shutdown(SocketShutdown.Both);
            _state.ServerSocket.Close();
        }

        public async Task<Result> SendTextMessageAsync(
            string message,
            string remoteServerIpAddress,
            int remoteServerPort,
            string localIpAddress,
            int localPort,
            CancellationToken token)
        {
            if (string.IsNullOrEmpty(message))
            {
                return Result.Fail("Message is null or empty string.");
            }

            var connectResult =
                await ConnectToServerAsync(remoteServerIpAddress, remoteServerPort, token);

            if (connectResult.Failure)
            {
                return connectResult;
            }

            EventOccurred?.Invoke(this,
            new ServerEvent
            {
                EventType = EventType.SendTextMessageStarted,
                TextMessage = message,
                RemoteServerIpAddress = remoteServerIpAddress,
                RemoteServerPortNumber = remoteServerPort
            });

            var messageData =
                MessageWrapper.ConstuctTextMessageRequest(message, localIpAddress, localPort);

            var sendMessageDataResult = await SendMessageData(messageData);
            if (sendMessageDataResult.Failure)
            {
                return sendMessageDataResult;
            }

            EventOccurred?.Invoke(this,
            new ServerEvent
                { EventType = EventType.SendTextMessageComplete });

            CloseServerSocket();

            return Result.Ok();
        }

        async Task<Result> SendOutboundFileTransferRequestAsync(
            string remoteServerIpAddress,
            int remoteServerPort,
            string localFilePath,
            string remoteFolderPath,
            CancellationToken token)
        {
            if (!File.Exists(localFilePath))
            {
                return Result.Fail("File does not exist: " + localFilePath);
            }

            var connectResult =
                await ConnectToServerAsync(remoteServerIpAddress, remoteServerPort, token);

            if (connectResult.Failure)
            {
                return connectResult;
            }
            
            var fileSizeBytes = new FileInfo(localFilePath).Length;
            EventOccurred?.Invoke(this,
            new ServerEvent
            {
                EventType = EventType.SendOutboundFileTransferInfoStarted,
                LocalIpAddress = _state.LocalIpAddress,
                LocalPortNumber = _state.LocalPort,
                LocalFolder = Path.GetDirectoryName(localFilePath),
                FileName = Path.GetFileName(localFilePath),
                FileSizeInBytes = fileSizeBytes,
                RemoteServerIpAddress = remoteServerIpAddress,
                RemoteServerPortNumber = remoteServerPort,
                RemoteFolder = remoteFolderPath
            });

            var messageData =
                MessageWrapper.ConstructOutboundFileTransferRequest(
                    localFilePath,
                    fileSizeBytes,
                    _state.LocalIpAddress,
                    _state.LocalPort,
                    remoteFolderPath);

            var sendMessageDataResult = await SendMessageData(messageData);
            if (sendMessageDataResult.Failure)
            {
                return sendMessageDataResult;
            }

            EventOccurred?.Invoke(this,
            new ServerEvent
            { EventType = EventType.SendOutboundFileTransferInfoComplete });

            return Result.Ok();
        }

        async Task<Result> SendFileBytesAsync(string localFilePath, CancellationToken token)
        {
            EventOccurred?.Invoke(this,
            new ServerEvent
                { EventType = EventType.SendFileBytesStarted });

            var fileSizeBytes = new FileInfo(localFilePath).Length;
            var bytesRemaining = fileSizeBytes;
            var fileChunkSentCount = 0;
            _state.FileTransferCanceled = false;

            using (var file = File.OpenRead(localFilePath))
            {
                while (bytesRemaining > 0)
                {
                    if (token.IsCancellationRequested)
                    {
                        token.ThrowIfCancellationRequested();
                    }

                    var fileChunkSize = (int) Math.Min(_state.BufferSize, bytesRemaining);
                    _state.Buffer = new byte[fileChunkSize];

                    var numberOfBytesToSend = file.Read(_state.Buffer, 0, fileChunkSize);
                    bytesRemaining -= numberOfBytesToSend;

                    var offset = 0;
                    var socketSendCount = 0;
                    while (numberOfBytesToSend > 0)
                    {
                        //SendFileChunk(offset, fileChunkSize);

                        var sendFileChunkResult =
                            await _state.ServerSocket.SendWithTimeoutAsync(
                                _state.Buffer,
                                offset,
                                fileChunkSize,
                                SocketFlags.None,
                                _state.SocketSettings.SendTimeoutMs).ConfigureAwait(false);

                        if (_state.FileTransferCanceled)
                        {
                            return Result.Fail(FileTransferStalledErrorMessage);
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

                    if (fileSizeBytes > (10 * _state.BufferSize)) continue;
                    SocketEventOccurred?.Invoke(this,
                    new ServerEvent
                    {
                        EventType = EventType.SentFileChunkToClient,
                        FileSizeInBytes = fileSizeBytes,
                        CurrentFileBytesSent = fileChunkSize,
                        FileBytesRemaining = bytesRemaining,
                        FileChunkSentCount = fileChunkSentCount,
                        SocketSendCount = socketSendCount
                    });
                }

                EventOccurred?.Invoke(this,
                new ServerEvent
                    { EventType = EventType.SendFileBytesComplete });

                var receiveConfirationMessageResult =
                    await ReceiveConfirmationAsync().ConfigureAwait(false);

                if (receiveConfirationMessageResult.Failure)
                {
                    return receiveConfirationMessageResult;
                }

                _state.ServerSocket.Shutdown(SocketShutdown.Both);
                _state.ServerSocket.Close();
                
                return Result.Ok();
            }
        }
        
        //void SendFileChunk(int offset, int fileBytesCount)
        //{
        //    _signalSendNextFileChunk.WaitOne();
        //    if (_state.FileTransferCanceled)
        //    {
        //        return;
        //    }

        //    _state.ServerSocket.BeginSend(
        //        _state.Buffer,
        //        offset,
        //        fileBytesCount,
        //        SocketFlags.None,
        //        SentFileChunk,
        //        null);
        //}

        //void SentFileChunk(IAsyncResult ar)
        //{
        //    _state.LastBytesSentCount = _state.ServerSocket.EndSend(ar);
        //    _signalSendNextFileChunk.Set();
        //}

        async Task<Result> ReceiveConfirmationAsync()
        {
            var buffer = new byte[_state.BufferSize];
            Result<int> receiveMessageResult;
            int bytesReceived;

            EventOccurred?.Invoke(this,
            new ServerEvent
            { EventType = EventType.ReceiveConfirmationMessageStarted });

            try
            {
                receiveMessageResult =
                    await _state.ServerSocket.ReceiveAsync(
                        buffer,
                        0,
                        _state.BufferSize,
                        0).ConfigureAwait(false);

                bytesReceived = receiveMessageResult.Value;
            }
            catch (SocketException ex)
            {
                _log.Error("Error raised in method ReceiveConfirmationAsync", ex);
                return Result.Fail($"{ex.Message} ({ex.GetType()} raised in method TplSocketServer.ReceiveConfirmationAsync)");
            }
            catch (TimeoutException ex)
            {
                _log.Error("Error raised in method ReceiveConfirmationAsync", ex);
                return Result.Fail($"{ex.Message} ({ex.GetType()} raised in method TplSocketServer.ReceiveConfirmationAsync)");
            }

            if (receiveMessageResult.Failure || bytesReceived == 0)
            {
                return Result.Fail("Error receiving confirmation message from remote server");
            }

            var confirmationMessage = Encoding.ASCII.GetString(buffer, 0, bytesReceived);

            if (confirmationMessage != ConfirmationMessage)
            {
                return Result.Fail($"Confirmation message doesn't match:\n\tExpected:\t{ConfirmationMessage}\n\tActual:\t{confirmationMessage}");
            }

            EventOccurred?.Invoke(this,
            new ServerEvent
            {
                EventType = EventType.ReceiveConfirmationMessageComplete,
                ConfirmationMessage = confirmationMessage
            });

            return Result.Ok();
        }

        public async Task<Result> GetFileAsync(
            string remoteServerIpAddress,
            int remoteServerPort,
            string remoteFilePath,
            string localIpAddress,
            int localPort,
            string localFolderPath,
            CancellationToken token)
        {
            var connectResult =
                await ConnectToServerAsync(remoteServerIpAddress, remoteServerPort, token);

            if (connectResult.Failure)
            {
                return connectResult;
            }

            EventOccurred?.Invoke(this,
            new ServerEvent
            {
                EventType = EventType.SendInboundFileTransferInfoStarted,
                RemoteServerIpAddress = remoteServerIpAddress,
                RemoteServerPortNumber = remoteServerPort,
                RemoteFolder = Path.GetDirectoryName(remoteFilePath),
                FileName = Path.GetFileName(remoteFilePath),
                LocalIpAddress = localIpAddress,
                LocalPortNumber = localPort,
                LocalFolder = localFolderPath,
            });

            var messageData =
                MessageWrapper.ConstructInboundFileTransferRequest(
                    remoteFilePath,
                    localIpAddress,
                    localPort,
                    localFolderPath);

            var sendMessageDataResult = await SendMessageData(messageData);
            if (sendMessageDataResult.Failure)
            {
                return sendMessageDataResult;
            }

            EventOccurred?.Invoke(this,
            new ServerEvent
                { EventType = EventType.SendInboundFileTransferInfoComplete });

            CloseServerSocket();

            return Result.Ok();
        }
        
        public async Task<Result> RequestFileListAsync(
            string remoteServerIpAddress,
            int remoteServerPort,
            string localIpAddress,
            int localPort,
            string targetFolder,
            CancellationToken token)
        {
            var connectResult =
                await ConnectToServerAsync(remoteServerIpAddress, remoteServerPort, token);

            if (connectResult.Failure)
            {
                return connectResult;
            }

            EventOccurred?.Invoke(this,
            new ServerEvent
            {
                EventType = EventType.SendFileListRequestStarted,
                LocalIpAddress = localIpAddress,
                LocalPortNumber = localPort,
                RemoteServerIpAddress = remoteServerIpAddress,
                RemoteServerPortNumber = remoteServerPort,
                RemoteFolder = targetFolder
            });

            var messageData =
                MessageWrapper.ConstructFileListRequest(localIpAddress, localPort, targetFolder);

            var sendMessageDataResult = await SendMessageData(messageData);
            if (sendMessageDataResult.Failure)
            {
                return sendMessageDataResult;
            }

            EventOccurred?.Invoke(this,
            new ServerEvent
                { EventType = EventType.SendFileListRequestComplete });

            CloseServerSocket();

            return Result.Ok();
        }

        public async Task<Result> RequestTransferFolderPathAsync(
            string remoteServerIpAddress,
            int remoteServerPort,
            string localIpAddress,
            int localPort,
            CancellationToken token)
        {
            return
                await SendSimpleMessageToClientAsync(
                    MessageType.TransferFolderPathRequest,
                    EventType.SendTransferFolderRequestStarted,
                    EventType.SendTransferFolderRequestComplete,
                    remoteServerIpAddress,
                    remoteServerPort,
                    localIpAddress,
                    localPort,
                    token);
        }

        public async Task<Result> RequestPublicIpAsync(
            string remoteServerIpAddress,
            int remoteServerPort,
            string localIpAddress,
            int localPort,
            CancellationToken token)
        {
            return
                await SendSimpleMessageToClientAsync(
                    MessageType.PublicIpAddressRequest,
                    EventType.SendPublicIpRequestStarted,
                    EventType.SendPublicIpRequestComplete,
                    remoteServerIpAddress,
                    remoteServerPort,
                    localIpAddress,
                    localPort,
                    token);
        }
        
        public async Task<Result> ShutdownServerAsync()
        {
            var shutdownResult = 
                await SendSimpleMessageToClientAsync(
                    MessageType.ShutdownServer,
                    EventType.SendShutdownServerCommandStarted,
                    EventType.SendShutdownServerCommandComplete,
                    _state.LocalIpAddress,
                    _state.LocalPort,
                    _state.LocalIpAddress,
                    _state.LocalPort,
                    new CancellationToken());

            if (shutdownResult.Failure)
            {
                return Result.Fail("Error occurred sending shutdown command to server");
            }
            
            _signalServerShutdown.WaitOne();

            return Result.Ok();
        }

        Result HandleShutdownServerCommand(byte[] messageData)
        {
            //TODO: Add logic that verifies command was sent by localIp, server should only be allowed to shutdown themselves and should generate a warning when a shutdown command is received from a remote server

            EventOccurred?.Invoke(this,
                new ServerEvent
                    { EventType = EventType.ReceiveShutdownServerCommandStarted });

            (string requestorIpAddress,
                int requestorPortNumber) = MessageUnwrapper.ReadServerConnectionInfo(messageData);

            _state.ShutdownServer = true;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ReceiveShutdownServerCommandComplete,
                    RemoteServerIpAddress = requestorIpAddress,
                    RemoteServerPortNumber = requestorPortNumber
                });

            return Result.Ok();
        }

        public Result CloseListenSocket()
        {
            EventOccurred?.Invoke(this,
            new ServerEvent
                { EventType = EventType.ShutdownListenSocketStarted });

            try
            {
                _state.ListenSocket.Shutdown(SocketShutdown.Both);
                _state.ListenSocket.Close();
            }
            catch (SocketException ex)
            {
                _log.Error("Error raised in method CloseListenSocket", ex);
                var errorMessage = $"{ex.Message} ({ex.GetType()} raised in method TplSocketServer.CloseListenSocket)";

                EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = EventType.ShutdownListenSocketCompletedWithError,
                    ErrorMessage = errorMessage
                });

                return Result.Ok(errorMessage);
            }

            EventOccurred?.Invoke(this,
            new ServerEvent
                { EventType = EventType.ShutdownListenSocketCompletedWithoutError });

            return Result.Ok();
        }
    }
}