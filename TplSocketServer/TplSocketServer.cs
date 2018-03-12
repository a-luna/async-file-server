namespace TplSocketServer
{
    using AaronLuna.Common.IO;
    using AaronLuna.Common.Network;
    using AaronLuna.Common.Result;

    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    public class TplSocketServer
    {
        const string ConfirmationMessage = "handshake";
        const string FileAlreadyExists = "A file with the same name already exists in the download folder, please rename or remove this file in order to proceed.";
        const string EmptyTransferFolderErrorMessage = "Currently there are no files available in transfer folder";
        const string FileTransferStalledErrorMessage = "Aborting file transfer, client says that data is no longer being received";

        readonly int _maxConnections;
        readonly int _bufferSize;
        readonly int _connectTimeoutMs;
        readonly int _receiveTimeoutMs;
        readonly int _sendTimeoutMs;

        string _localIpAddress;
        int _localPort;
        readonly Socket _listenSocket;
        Socket _clientSocket;
        Socket _serverSocket;
        byte[] _buffer;
        List<byte> _unreadBytes;
        int _lastBytesReceivedCount;
        private int _lastBytesSentCount;
        bool _fileTransferIsStalled;
        readonly AutoResetEvent _sendSync = new AutoResetEvent(true);

        public TplSocketServer()
        {
            CidrMask = "192.168.2.0/24";

            _localIpAddress = GetLocalIpAddress();
            TransferFolderPath = GetDefaultTransferFolder();

            _maxConnections = 5;
            _bufferSize = 1024;
            _connectTimeoutMs = 5000;
            _receiveTimeoutMs = 5000;
            _sendTimeoutMs = 5000;

            _listenSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        }

        public TplSocketServer(AppSettings appSettings, IPAddress localIpAddress)
        {
            CidrMask = "192.168.2.0/24";

            _localIpAddress = localIpAddress.ToString();
            TransferFolderPath = appSettings.TransferFolderPath;

            if (!Directory.Exists(TransferFolderPath))
            {
                Directory.CreateDirectory(TransferFolderPath);
            }

            _maxConnections = appSettings.SocketSettings.MaxNumberOfConections;
            _bufferSize = appSettings.SocketSettings.BufferSize;
            _connectTimeoutMs = appSettings.SocketSettings.ConnectTimeoutMs;
            _receiveTimeoutMs = appSettings.SocketSettings.ReceiveTimeoutMs;
            _sendTimeoutMs = appSettings.SocketSettings.SendTimeoutMs;

            _listenSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        }

        public string CidrMask { get; set; }
        public string TransferFolderPath { get; set; }

        public event EventHandler<ServerEventArgs> EventOccurred;

        string GetLocalIpAddress()
        {
            var localIp = string.Empty;
            var acquiredLocalIp = Network.GetLocalIPv4AddressFromInternet();
            if (acquiredLocalIp.Failure)
            {
                var localIps = Network.GetLocalIPv4AddressList();
                foreach (var ip in localIps)
                {
                    var result = Network.IpAddressIsInCidrRange(ip.ToString(), CidrMask);
                    if (result.Success && result.Value)
                    {
                        localIp = ip.ToString();
                        break;
                    }
                }

                if (string.IsNullOrEmpty(localIp) && localIps.Count > 0)
                {
                    localIp = localIps[0].ToString();
                }
            }
            else
            {
                localIp = acquiredLocalIp.Value.ToString();
            }

            return localIp;
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

        public async Task<Result> HandleIncomingConnectionsAsync(string localIpAddress, int localPort,
            CancellationToken token)
        {
            _localIpAddress = localIpAddress;
            _localPort = localPort;

            return (await Task.Factory.StartNew(() =>
                    Listen(localIpAddress, localPort), token).ConfigureAwait(false))
                .OnSuccess(() =>
                    WaitForConnectionsAsync(token));
        }

        public async Task<Result> HandleIncomingConnectionsAsync(int localPort, CancellationToken token)
        {
            _localPort = localPort;

            return (await Task.Factory.StartNew(() =>
                    Listen(string.Empty, _localPort), token).ConfigureAwait(false))
                .OnSuccess(() =>
                    WaitForConnectionsAsync(token));
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
            new ServerEventArgs
            { EventType = ServerEventType.ListenOnLocalPortStarted });

            var ipEndPoint = new IPEndPoint(ipToBind, localPort);
            try
            {
                _listenSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _listenSocket.Bind(ipEndPoint);
                _listenSocket.Listen(_maxConnections);
            }
            catch (SocketException ex)
            {
                return Result.Fail($"{ex.Message} ({ex.GetType()} raised in method TplSocketServer.Listen)");
            }

            EventOccurred?.Invoke(this,
            new ServerEventArgs
            { EventType = ServerEventType.ListenOnLocalPortCompleted });

            return Result.Ok();
        }

        async Task<Result> WaitForConnectionsAsync(CancellationToken token)
        {
            // Main loop. Server handles incoming connections until encountering an error
            while (true)
            {
                if (token.IsCancellationRequested)
                {
                    token.ThrowIfCancellationRequested();
                }

                EventOccurred?.Invoke(this,
                new ServerEventArgs
                { EventType = ServerEventType.AcceptConnectionAttemptStarted });

                var acceptResult = await _listenSocket.AcceptTaskAsync().ConfigureAwait(false);
                if (acceptResult.Failure)
                {
                    return acceptResult;
                }

                _clientSocket = acceptResult.Value;
                EventOccurred?.Invoke(this,
                new ServerEventArgs
                { EventType = ServerEventType.AcceptConnectionAttemptCompleted });

                var clientRequest = await HandleClientRequestAsync(token).ConfigureAwait(false);
                if (clientRequest.Success) continue;

                EventOccurred?.Invoke(this,
                new ServerEventArgs
                {
                    EventType = ServerEventType.ErrorOccurred,
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

            var requestResult = await ReadIncomingMessageAsync(token).ConfigureAwait(false);
            try
            {
                _clientSocket.Shutdown(SocketShutdown.Both);
                _clientSocket.Close();
            }
            catch (SocketException ex)
            {
                return Result.Fail($"{ex.Message} ({ex.GetType()} raised in method TplSocketServer.CloseListenSocket)");
            }

            return requestResult;
        }

        async Task<Result> ReadIncomingMessageAsync(CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
            }

            _buffer = new byte[_bufferSize];
            _unreadBytes = new List<byte>();

            EventOccurred?.Invoke(this,
            new ServerEventArgs
            { EventType = ServerEventType.DetermineMessageLengthStarted });

            var determineMessageLengthResult = await DetermineMessageLengthAsync().ConfigureAwait(false);
            if (determineMessageLengthResult.Failure)
            {
                return determineMessageLengthResult;
            }

            var messageLength = determineMessageLengthResult.Value;

            EventOccurred?.Invoke(this,
            new ServerEventArgs
            {
                EventType = ServerEventType.DetermineMessageLengthCompleted,
                TotalBytesInMessage = messageLength
            });

            EventOccurred?.Invoke(this,
            new ServerEventArgs
            { EventType = ServerEventType.ReceiveAllMessageBytesStarted });

            var receiveMessageResult = await ReceiveAllMessageBytesAsync(messageLength).ConfigureAwait(false);
            if (receiveMessageResult.Failure)
            {
                return receiveMessageResult;
            }

            var messageData = receiveMessageResult.Value;

            EventOccurred?.Invoke(this,
            new ServerEventArgs
                { EventType = ServerEventType.ReceiveAllMessageBytesCompleted });

            return await ProcessRequestAsync(messageData, token).ConfigureAwait(false);
        }

        async Task<Result<int>> ReadFromSocketAsync()
        {
            Result<int> receiveResult;
            int bytesReceived;

            try
            {
                receiveResult =
                    await _clientSocket.ReceiveWithTimeoutAsync(
                            _buffer,
                            0,
                            _bufferSize,
                            0,
                            _receiveTimeoutMs).ConfigureAwait(false);

                bytesReceived = receiveResult.Value;
            }
            catch (SocketException ex)
            {
                return Result.Fail<int>($"{ex.Message} ({ex.GetType()} raised in method TplSocketServer.DetermineTransferTypeAsync)");
            }
            catch (TimeoutException ex)
            {
                return Result.Fail<int>($"{ex.Message} ({ex.GetType()} raised in method TplSocketServer.DetermineTransferTypeAsync)");
            }

            if (receiveResult.Failure)
            {
                return Result.Fail<int>(receiveResult.Error);
            }

            return bytesReceived > 0
                ? Result.Ok(bytesReceived)
                : Result.Fail<int>("Error reading request from client, no data was received");
        }

        async Task<Result<int>> DetermineMessageLengthAsync()
        {
            var readFromSocketResult = await ReadFromSocketAsync().ConfigureAwait(false);
            if (readFromSocketResult.Failure)
            {
                return readFromSocketResult;
            }

            _lastBytesReceivedCount = readFromSocketResult.Value;
            if (_lastBytesReceivedCount > 4)
            {
                var numberOfUnreadBytes = _lastBytesReceivedCount - 4;
                var unreadBytes = new byte[numberOfUnreadBytes];
                _buffer.ToList().CopyTo(4, unreadBytes, 0, numberOfUnreadBytes);
                _unreadBytes = unreadBytes.ToList();

                EventOccurred?.Invoke(this,
                new ServerEventArgs
                {
                    EventType = ServerEventType.LastSocketReadContainedUnreadBytes,
                    UnreadByteCount = numberOfUnreadBytes
                });
            }

            var messageLength = MessageUnwrapper.ReadInt32(_buffer);

            return Result.Ok(messageLength);
        }

        async Task<Result<byte[]>> ReceiveAllMessageBytesAsync(int messageLength)
        {
            var arraySize = 0;
            var socketReadCount = 0;
            var totalBytesReceived = 0;
            var bytesRemaining = messageLength;
            var messageData = new List<byte>();

            if (_unreadBytes.Count > 0)
            {
                arraySize = Math.Min(messageLength, _unreadBytes.Count);
                var unreadMessageBytes = new byte[arraySize];
                _unreadBytes.CopyTo(0, unreadMessageBytes, 0, arraySize);
                messageData.AddRange(unreadMessageBytes.ToList());
                totalBytesReceived += messageData.Count;
                bytesRemaining -= messageData.Count;
                
                EventOccurred?.Invoke(this,
                new ServerEventArgs
                {
                    EventType = ServerEventType.AppendUnreadBytesToMessageData,
                    CurrentMessageBytesReceived = totalBytesReceived,
                    TotalMessageBytesReceived = totalBytesReceived,
                    TotalBytesInMessage = messageLength,
                    MessageBytesRemaining = bytesRemaining
                });

                if (_unreadBytes.Count > messageLength)
                {
                    arraySize = _unreadBytes.Count - messageLength;
                    var savedBytes = new byte[arraySize];
                    _unreadBytes.CopyTo(messageLength, savedBytes, 0, arraySize);
                    _unreadBytes = savedBytes.ToList();
                }
                else
                {
                    _unreadBytes = new List<byte>();
                }
            }

            arraySize = 0;
            var unreadByteCount = 0;
            while (bytesRemaining > 0)
            {
                var readFromSocketResult = await ReadFromSocketAsync().ConfigureAwait(false);
                if (readFromSocketResult.Failure)
                {
                    return Result.Fail<byte[]>(readFromSocketResult.Error);
                }

                _lastBytesReceivedCount = readFromSocketResult.Value;
                arraySize = Math.Min(bytesRemaining, _lastBytesReceivedCount);
                var receivedBytes = new byte[arraySize];
                unreadByteCount = _lastBytesReceivedCount - arraySize;

                _buffer.ToList().CopyTo(0, receivedBytes, 0, arraySize);
                messageData.AddRange(receivedBytes.ToList());
                socketReadCount++;
                totalBytesReceived += arraySize;
                bytesRemaining -= arraySize;

                EventOccurred?.Invoke(this,
                new ServerEventArgs
                {
                    EventType = ServerEventType.ReceivedClientMessageDataFromSocket,
                    SocketReadCount = socketReadCount,
                    CurrentMessageBytesReceived = _lastBytesReceivedCount,
                    TotalMessageBytesReceived = arraySize,
                    TotalBytesInMessage = messageLength,
                    MessageBytesRemaining = bytesRemaining
                });
            }
            
            if (unreadByteCount <= 0)
            {
                return Result.Ok(messageData.ToArray());
            }

            EventOccurred?.Invoke(this,
            new ServerEventArgs
            {
                EventType = ServerEventType.LastSocketReadContainedUnreadBytes,
                UnreadByteCount = unreadByteCount
            });

            var unreadBytes = new byte[unreadByteCount];
            _buffer.ToList().CopyTo(arraySize, unreadBytes, 0, unreadByteCount);
            _unreadBytes = unreadBytes.ToList();

            return Result.Ok(messageData.ToArray());
        }

        async Task<Result> ProcessRequestAsync(byte[] messageData, CancellationToken token)
        {
            EventOccurred?.Invoke(this,
            new ServerEventArgs
                { EventType = ServerEventType.DetermineRequestTypeStarted });

            var transferTypeData = MessageUnwrapper.ReadInt32(messageData).ToString();
            var transferType = (RequestType)Enum.Parse(typeof(RequestType), transferTypeData);

            switch (transferType)
            {
                case RequestType.TextMessage:

                    EventOccurred?.Invoke(this,
                    new ServerEventArgs
                    {
                        EventType = ServerEventType.DetermineRequestTypeCompleted,
                        RequestType = RequestType.TextMessage
                    });

                    return ReceiveTextMessage(messageData, token);

                case RequestType.InboundFileTransfer:

                    EventOccurred?.Invoke(this,
                    new ServerEventArgs
                    {
                        EventType = ServerEventType.DetermineRequestTypeCompleted,
                        RequestType = RequestType.InboundFileTransfer
                    });

                    return await InboundFileTransferAsync(messageData, token).ConfigureAwait(false);

                case RequestType.OutboundFileTransfer:

                    EventOccurred?.Invoke(this,
                    new ServerEventArgs
                    {
                        EventType = ServerEventType.DetermineRequestTypeCompleted,
                        RequestType = RequestType.OutboundFileTransfer
                    });

                    return await OutboundFileTransferAsync(messageData, token).ConfigureAwait(false);

                case RequestType.DataIsNoLongerBeingReceived:

                    EventOccurred?.Invoke(this,
                    new ServerEventArgs
                    {
                        EventType = ServerEventType.DetermineMessageLengthCompleted,
                        RequestType = RequestType.DataIsNoLongerBeingReceived
                    });

                    return AbortOutboundFileTransfer(messageData, token);

                case RequestType.GetFileList:

                    EventOccurred?.Invoke(this,
                    new ServerEventArgs
                    {
                        EventType = ServerEventType.DetermineRequestTypeCompleted,
                        RequestType = RequestType.GetFileList
                    });

                    return await SendFileList(messageData, token).ConfigureAwait(false);

                case RequestType.ReceiveFileList:

                    EventOccurred?.Invoke(this,
                    new ServerEventArgs
                    {
                        EventType = ServerEventType.DetermineRequestTypeCompleted,
                        RequestType = RequestType.ReceiveFileList
                    });

                    return ReceiveFileList(messageData, token);

                case RequestType.TransferFolderPathRequest:

                    EventOccurred?.Invoke(this,
                    new ServerEventArgs
                    {
                        EventType = ServerEventType.DetermineRequestTypeCompleted,
                        RequestType = RequestType.TransferFolderPathRequest
                    });

                    return await SendTransferFolderResponseAsync(messageData, token).ConfigureAwait(false);

                case RequestType.TransferFolderPathResponse:

                    EventOccurred?.Invoke(this,
                    new ServerEventArgs
                    {
                        EventType = ServerEventType.DetermineRequestTypeCompleted,
                        RequestType = RequestType.TransferFolderPathResponse
                    });

                    return ReceiveTransferFolderResponse(messageData, token);

                case RequestType.PublicIpAddressRequest:

                    EventOccurred?.Invoke(this,
                    new ServerEventArgs
                    {
                        EventType = ServerEventType.DetermineRequestTypeCompleted,
                        RequestType = RequestType.PublicIpAddressRequest
                    });

                    return await SendPublicIpAddress(messageData, token).ConfigureAwait(false);

                case RequestType.PublicIpAddressResponse:

                    EventOccurred?.Invoke(this,
                    new ServerEventArgs
                    {
                        EventType = ServerEventType.DetermineRequestTypeCompleted,
                        RequestType = RequestType.PublicIpAddressResponse
                    });

                    return ReceivePublicIpAddress(messageData, token);

                default:

                    var error = $"Unable to determine transfer type, value of '{transferType}' is invalid.";
                    return Result.Fail(error);
            }
        }

        Result ReceiveTextMessage(byte[] messageData, CancellationToken token)
        {
            EventOccurred?.Invoke(this,
            new ServerEventArgs
            { EventType = ServerEventType.ReadTextMessageStarted });

            var (message,
                remoteIpAddress,
                remotePortNumber) = MessageUnwrapper.ReadTextMessage(messageData);

            if (token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
            }

            EventOccurred?.Invoke(this,
            new ServerEventArgs
            {
                EventType = ServerEventType.ReadTextMessageCompleted,
                TextMessage = message,
                RemoteServerIpAddress = remoteIpAddress,
                RemoteServerPortNumber = remotePortNumber
            });

            return Result.Ok();
        }

        async Task<Result> InboundFileTransferAsync(byte[] buffer, CancellationToken token)
        {
            EventOccurred?.Invoke(this,
            new ServerEventArgs
            { EventType = ServerEventType.ReadInboundFileTransferInfoStarted });

            var (localFilePath,
                fileSizeBytes,
                remoteIpAddress,
                remotePort) = MessageUnwrapper.ReadInboundFileTransferRequest(buffer);

            if (token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
            }

            EventOccurred?.Invoke(this,
            new ServerEventArgs
            {
                EventType = ServerEventType.ReadInboundFileTransferInfoCompleted,
                LocalFolder = Path.GetDirectoryName(localFilePath),
                FileName = Path.GetFileName(localFilePath),
                FileSizeInBytes = fileSizeBytes,
                RemoteServerIpAddress = remoteIpAddress,
                RemoteServerPortNumber = remotePort
            });

            if (File.Exists(localFilePath))
            {
                var message = $"{FileAlreadyExists} ({localFilePath})";

                var sendResult = await SendTextMessageAsync(
                        message,
                        remoteIpAddress,
                        remotePort,
                        _localIpAddress,
                        _localPort,
                        token)
                    .ConfigureAwait(false);

                return sendResult.Success
                    ? Result.Ok()
                    : sendResult;
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
            new ServerEventArgs
            {
                EventType = ServerEventType.SendConfirmationMessageStarted,
                ConfirmationMessage = ConfirmationMessage
            });

            var confirmationMessageData = Encoding.ASCII.GetBytes(ConfirmationMessage);

            var sendConfirmatinMessageResult =
                await _clientSocket.SendWithTimeoutAsync(
                        confirmationMessageData,
                        0,
                        confirmationMessageData.Length,
                        0,
                        _sendTimeoutMs).ConfigureAwait(false);

            if (sendConfirmatinMessageResult.Failure)
            {
                return sendConfirmatinMessageResult;
            }

            EventOccurred?.Invoke(this,
            new ServerEventArgs
            { EventType = ServerEventType.SendConfirmationMessageCompleted });

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
            float percentComplete = 0;
            _fileTransferIsStalled = false;

            if (_unreadBytes.Count > 0)
            {
                totalBytesReceived += _unreadBytes.Count;

                var writeBytesResult =
                    FileHelper.WriteBytesToFile(
                        localFilePath,
                        _unreadBytes.ToArray(),
                        _unreadBytes.Count);

                if (writeBytesResult.Failure)
                {
                    return writeBytesResult;
                }

                EventOccurred?.Invoke(this,
                new ServerEventArgs
                {
                    EventType = ServerEventType.AppendUnreadBytesToInboundFileTransfer,
                    CurrentFileBytesReceived = _unreadBytes.Count,
                    TotalFileBytesReceived = totalBytesReceived,
                    FileSizeInBytes = fileSizeInBytes,
                    FileBytesRemaining = fileSizeInBytes - totalBytesReceived
                });
            }

            var startTime = DateTime.Now;
            EventOccurred?.Invoke(this,
            new ServerEventArgs
            {
                EventType = ServerEventType.ReceiveFileBytesStarted,
                FileTransferStartTime = startTime,
                FileSizeInBytes = fileSizeInBytes
            });

            // Read file bytes from transfer socket until 
            //      1. the entire file has been received OR 
            //      2. Data is no longer being received OR
            //      3, Transfer is cancelled
            while (totalBytesReceived != fileSizeInBytes)
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

                _lastBytesReceivedCount = readFromSocketResult.Value;
                var receivedBytes = new byte[_lastBytesReceivedCount];                
                if (_lastBytesReceivedCount == 0)
                {
                    return Result.Fail("Socket is no longer receiving data, must abort file transfer");
                }

                var writeBytesResult =
                    FileHelper.WriteBytesToFile(localFilePath, receivedBytes, _lastBytesReceivedCount);

                if (writeBytesResult.Failure)
                {
                    return writeBytesResult;
                }

                receiveCount++;
                totalBytesReceived += _lastBytesReceivedCount;
                var bytesRemaining = fileSizeInBytes - totalBytesReceived;
                var checkPercentComplete = totalBytesReceived / (float)fileSizeInBytes;
                var changeSinceLastUpdate = checkPercentComplete - percentComplete;

                // this method fires on every socket read event, which could be hurdreds of thousands
                // of times or millions of times dependingon the file size. Since this event is only 
                // being used by myself when debugging small test files, I limited this event to only 
                // fire when the size of the incoming file is less than 200 KB
                if (fileSizeInBytes < (10 * 1024))
                {
                    EventOccurred?.Invoke(this,
                    new ServerEventArgs
                    {
                        EventType = ServerEventType.ReceivedFileBytesFromSocket,
                        SocketReadCount = receiveCount,
                        CurrentFileBytesReceived = _lastBytesReceivedCount,
                        TotalFileBytesReceived = totalBytesReceived,
                        FileSizeInBytes = fileSizeInBytes,
                        FileBytesRemaining = bytesRemaining,
                        PercentComplete = percentComplete
                    });
                }
                
                // Report progress only if at least 1% of file has been received since the last update
                if (changeSinceLastUpdate > (float).01)
                {
                    percentComplete = checkPercentComplete;
                    EventOccurred?.Invoke(this,
                    new ServerEventArgs
                    {
                        EventType = ServerEventType.FileTransferProgress,
                        TotalFileBytesReceived = totalBytesReceived,
                        FileSizeInBytes = fileSizeInBytes,
                        FileBytesRemaining = bytesRemaining,
                        PercentComplete = percentComplete
                    });
                }
            }

            if (_fileTransferIsStalled)
            {
                return Result.Fail("Data is no longer bring received from remote client, file transfer has been cancelled");
            }

            EventOccurred?.Invoke(this,
            new ServerEventArgs
            {
                EventType = ServerEventType.ReceiveFileBytesCompleted,
                FileTransferStartTime = startTime,
                FileTransferCompleteTime = DateTime.Now,
                FileSizeInBytes = fileSizeInBytes,
                RemoteServerIpAddress = remoteIpAddress,
                RemoteServerPortNumber = remotePort
            });

            return Result.Ok();
        }

        async Task<Result> OutboundFileTransferAsync(byte[] buffer, CancellationToken token)
        {
            EventOccurred?.Invoke(this,
            new ServerEventArgs
            { EventType = ServerEventType.ReadOutboundFileTransferInfoStarted });

            var (requestedFilePath,
                remoteServerIpAddress,
                remoteServerPort,
                remoteFolderPath) = MessageUnwrapper.ReadOutboundFileTransferRequest(buffer);

            EventOccurred?.Invoke(this,
            new ServerEventArgs
            {
                EventType = ServerEventType.ReadOutboundFileTransferInfoCompleted,
                LocalFolder = Path.GetDirectoryName(requestedFilePath),
                FileName = Path.GetFileName(requestedFilePath),
                FileSizeInBytes = new FileInfo(requestedFilePath).Length,
                RemoteFolder = remoteFolderPath,
                RemoteServerIpAddress = remoteServerIpAddress,
                RemoteServerPortNumber = remoteServerPort
            });

            if (!File.Exists(requestedFilePath))
            {
                return Result.Fail("File does not exist: " + requestedFilePath);
            }

            if (token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
            }

            return await
                SendFileAsync(
                        remoteServerIpAddress,
                        remoteServerPort,
                        requestedFilePath,
                        remoteFolderPath,
                        token).ConfigureAwait(false);
        }

        Result AbortOutboundFileTransfer(byte[] messageData, CancellationToken token)
        {
            (string requestorIpAddress,
                int requestorPortNumber) = MessageUnwrapper.ReadServerConnectionInfo(messageData);

            EventOccurred?.Invoke(this,
            new ServerEventArgs
            {
                EventType = ServerEventType.AbortOutboundFileTransfer,
                RemoteServerIpAddress = requestorIpAddress,
                RemoteServerPortNumber = requestorPortNumber
            });

            _fileTransferIsStalled = true;
            _sendSync.Set();

            return Result.Ok();
        }

        async Task<Result> SendFileList(byte[] buffer, CancellationToken token)
        {
            EventOccurred?.Invoke(this,
            new ServerEventArgs
            { EventType = ServerEventType.ReadFileListRequestStarted });

            (string requestorIpAddress,
                int requestorPortNumber,
                string targetFolderPath) = MessageUnwrapper.ReadFileListRequest(buffer);

            EventOccurred?.Invoke(this,
            new ServerEventArgs
            {
                EventType = ServerEventType.ReadFileListRequestCompleted,
                RemoteServerIpAddress = requestorIpAddress,
                RemoteServerPortNumber = requestorPortNumber,
                RemoteFolder = targetFolderPath
            });

            List<string> listOfFiles;
            try
            {
                listOfFiles = Directory.GetFiles(TransferFolderPath).ToList();
            }
            catch (IOException ex)
            {
                return Result.Fail($"{ex.Message} ({ex.GetType()})");
            }

            if (listOfFiles.Count == 0)
            {
                EventOccurred?.Invoke(this,
                new ServerEventArgs
                {
                    EventType = ServerEventType.SendFileListResponseStarted,
                    RemoteServerIpAddress = _localIpAddress,
                    RemoteServerPortNumber = _localPort,
                    FileInfoList = new List<(string, long)>(),
                    LocalFolder = targetFolderPath,
                });

                var message = $"{EmptyTransferFolderErrorMessage}: {TransferFolderPath}";

                var sendResult =
                    await SendTextMessageAsync(
                        message,
                        requestorIpAddress,
                        requestorPortNumber,
                        _localIpAddress,
                        _localPort,
                        token).ConfigureAwait(false);

                EventOccurred?.Invoke(this,
                new ServerEventArgs
                { EventType = ServerEventType.SendFileListResponseCompleted });

                return sendResult.Success
                    ? Result.Ok()
                    : sendResult;
            }

            if (token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
            }

            var fileInfoList = new List<(string, long)>();
            foreach (var file in listOfFiles)
            {
                var fileSize = new FileInfo(file).Length;
                fileInfoList.Add((file, fileSize));
            }

            EventOccurred?.Invoke(this,
            new ServerEventArgs
            { EventType = ServerEventType.ConnectToRemoteServerStarted });

            var transferSocket =
                new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            var connectResult =
                await transferSocket.ConnectWithTimeoutAsync(
                        requestorIpAddress,
                        requestorPortNumber,
                        _connectTimeoutMs).ConfigureAwait(false);

            if (connectResult.Failure)
            {
                return connectResult;
            }

            EventOccurred?.Invoke(this,
            new ServerEventArgs
            { EventType = ServerEventType.ConnectToRemoteServerCompleted });

            EventOccurred?.Invoke(this,
            new ServerEventArgs
            {
                EventType = ServerEventType.SendFileListResponseStarted,
                RemoteServerIpAddress = _localIpAddress,
                RemoteServerPortNumber = _localPort,
                FileInfoList = fileInfoList,
                LocalFolder = targetFolderPath,
            });

            var messageData =
                MessageWrapper.ConstructFileListResponse(
                    fileInfoList,
                    "*",
                    "|",
                    _localIpAddress,
                    _localPort,
                    requestorIpAddress,
                    requestorPortNumber,
                    targetFolderPath);

            var messageLength = BitConverter.GetBytes(messageData.Length);

            var sendMessageResult =
                await transferSocket.SendWithTimeoutAsync(
                        messageLength,
                        0,
                        messageLength.Length,
                        0,
                        _sendTimeoutMs).ConfigureAwait(false);

            if (sendMessageResult.Failure)
            {
                return sendMessageResult;
            }

            sendMessageResult =
                await transferSocket.SendWithTimeoutAsync(
                        messageData,
                        0,
                        messageData.Length,
                        0,
                        _sendTimeoutMs).ConfigureAwait(false);

            if (sendMessageResult.Failure)
            {
                return sendMessageResult;
            }

            EventOccurred?.Invoke(this,
            new ServerEventArgs
            { EventType = ServerEventType.SendFileListResponseCompleted });

            transferSocket.Shutdown(SocketShutdown.Both);
            transferSocket.Close();

            return Result.Ok();
        }

        Result ReceiveFileList(byte[] buffer, CancellationToken token)
        {
            EventOccurred?.Invoke(this,
            new ServerEventArgs
            { EventType = ServerEventType.ReadFileListResponseStarted });

            var (remoteServerIp,
                remoteServerPort,
                localIp,
                localPort,
                transferFolder,
                fileInfoList) = MessageUnwrapper.ReadFileListResponse(buffer);

            if (token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
            }

            EventOccurred?.Invoke(this,
            new ServerEventArgs
            {
                EventType = ServerEventType.ReadFileListResponseCompleted,
                RemoteServerIpAddress = remoteServerIp,
                RemoteServerPortNumber = remoteServerPort,
                LocalIpAddress = localIp,
                LocalPortNumber = localPort,
                LocalFolder = transferFolder,
                FileInfoList = fileInfoList
            });

            return Result.Ok();
        }

        async Task<Result> SendTransferFolderResponseAsync(byte[] buffer, CancellationToken token)
        {
            EventOccurred?.Invoke(this,
            new ServerEventArgs
            { EventType = ServerEventType.ReadTransferFolderRequestStarted });

            (string requestorIpAddress,
                int requestorPortNumber) = MessageUnwrapper.ReadServerConnectionInfo(buffer);

            EventOccurred?.Invoke(this,
            new ServerEventArgs
            {
                EventType = ServerEventType.ReadTransferFolderRequestCompleted,
                RemoteServerIpAddress = requestorIpAddress,
                RemoteServerPortNumber = requestorPortNumber
            });

            if (token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
            }

            EventOccurred?.Invoke(this,
            new ServerEventArgs
            { EventType = ServerEventType.ConnectToRemoteServerStarted });

            var transferSocket =
                new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            var connectResult =
                await transferSocket.ConnectWithTimeoutAsync(
                        requestorIpAddress,
                        requestorPortNumber,
                        _connectTimeoutMs).ConfigureAwait(false);

            if (connectResult.Failure)
            {
                return connectResult;
            }

            EventOccurred?.Invoke(this,
            new ServerEventArgs
            { EventType = ServerEventType.ConnectToRemoteServerCompleted });

            EventOccurred?.Invoke(this,
            new ServerEventArgs
            {
                EventType = ServerEventType.SendTransferFolderResponseStarted,
                RemoteServerIpAddress = requestorIpAddress,
                RemoteServerPortNumber = requestorPortNumber,
                LocalFolder = TransferFolderPath
            });

            var messageData =
                MessageWrapper.ConstructTransferFolderResponse(
                    _localIpAddress,
                    _localPort,
                    TransferFolderPath);

            var messageLength = BitConverter.GetBytes(messageData.Length);

            var sendMessageResult =
                await transferSocket.SendWithTimeoutAsync(
                        messageLength,
                        0,
                        messageLength.Length,
                        0,
                        _sendTimeoutMs).ConfigureAwait(false);

            if (sendMessageResult.Failure)
            {
                return sendMessageResult;
            }

            sendMessageResult =
                await transferSocket.SendWithTimeoutAsync(
                        messageData,
                        0,
                        messageData.Length,
                        0,
                        _sendTimeoutMs).ConfigureAwait(false);

            if (sendMessageResult.Failure)
            {
                return sendMessageResult;
            }

            EventOccurred?.Invoke(this,
            new ServerEventArgs
            { EventType = ServerEventType.SendTransferFolderRequestCompleted });

            transferSocket.Shutdown(SocketShutdown.Both);
            transferSocket.Close();

            return Result.Ok();
        }

        Result ReceiveTransferFolderResponse(byte[] buffer, CancellationToken token)
        {
            EventOccurred?.Invoke(this,
            new ServerEventArgs
            { EventType = ServerEventType.ReadTransferFolderResponseStarted });

            var (remoteServerIp,
                remoteServerPort,
                transferFolder) = MessageUnwrapper.ReadTransferFolderResponse(buffer);

            if (token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
            }

            EventOccurred?.Invoke(this,
            new ServerEventArgs
            {
                EventType = ServerEventType.ReadTransferFolderResponseCompleted,
                RemoteServerIpAddress = remoteServerIp,
                RemoteServerPortNumber = remoteServerPort,
                RemoteFolder = transferFolder
            });

            return Result.Ok();
        }

        async Task<Result> SendPublicIpAddress(byte[] buffer, CancellationToken token)
        {
            EventOccurred?.Invoke(this,
            new ServerEventArgs
            { EventType = ServerEventType.ReadTransferFolderRequestStarted });

            (string requestorIpAddress,
                int requestorPortNumber) = MessageUnwrapper.ReadServerConnectionInfo(buffer);

            EventOccurred?.Invoke(this,
            new ServerEventArgs
            {
                EventType = ServerEventType.ReadPublicIpRequestCompleted,
                RemoteServerIpAddress = requestorIpAddress,
                RemoteServerPortNumber = requestorPortNumber
            });

            if (token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
            }

            EventOccurred?.Invoke(this,
            new ServerEventArgs
            { EventType = ServerEventType.ConnectToRemoteServerStarted });

            var transferSocket =
                new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            var connectResult =
                await transferSocket.ConnectWithTimeoutAsync(
                        requestorIpAddress,
                        requestorPortNumber,
                        _connectTimeoutMs).ConfigureAwait(false);

            if (connectResult.Failure)
            {
                return connectResult;
            }

            EventOccurred?.Invoke(this,
            new ServerEventArgs
            { EventType = ServerEventType.ConnectToRemoteServerCompleted });

            var publicIp = IPAddress.None.ToString();
            var publicIpResult = await Network.GetPublicIPv4AddressAsync().ConfigureAwait(false);
            if (publicIpResult.Success)
            {
                publicIp = publicIpResult.Value.ToString();
            }

            EventOccurred?.Invoke(this,
            new ServerEventArgs
            {
                EventType = ServerEventType.SendPublicIpResponseStarted,
                RemoteServerIpAddress = _localIpAddress,
                RemoteServerPortNumber = _localPort,
                PublicIpAddress = publicIp
            });

            var messageData =
                MessageWrapper.ConstructPublicIpAddressResponse(
                    _localIpAddress,
                    _localPort,
                    publicIp);

            var messageLength = BitConverter.GetBytes(messageData.Length);

            var sendMessageResult =
                await transferSocket.SendWithTimeoutAsync(
                        messageLength,
                        0,
                        messageLength.Length,
                        0,
                        _sendTimeoutMs).ConfigureAwait(false);

            if (sendMessageResult.Failure)
            {
                return sendMessageResult;
            }

            sendMessageResult =
                await transferSocket.SendWithTimeoutAsync(
                        messageData,
                        0,
                        messageData.Length,
                        0,
                        _sendTimeoutMs).ConfigureAwait(false);

            if (sendMessageResult.Failure)
            {
                return sendMessageResult;
            }

            EventOccurred?.Invoke(this,
            new ServerEventArgs
            { EventType = ServerEventType.SendPublicIpResponseCompleted });

            transferSocket.Shutdown(SocketShutdown.Both);
            transferSocket.Close();

            return Result.Ok();
        }

        Result ReceivePublicIpAddress(byte[] buffer, CancellationToken token)
        {
            EventOccurred?.Invoke(this,
            new ServerEventArgs
            { EventType = ServerEventType.ReadPublicIpResponseStarted });

            var (remoteServerIp,
                remoteServerPort,
                publicIpAddress) = MessageUnwrapper.ReadPublicIpAddressResponse(buffer);

            if (token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
            }

            EventOccurred?.Invoke(this,
            new ServerEventArgs
            {
                EventType = ServerEventType.ReadPublicIpResponseCompleted,
                RemoteServerIpAddress = remoteServerIp,
                RemoteServerPortNumber = remoteServerPort,
                PublicIpAddress = publicIpAddress
            });

            return Result.Ok();
        }

        async Task<Result> SendGenericMessageToClient(
            RequestType requestType,
            ServerEventType eventStarted,
            ServerEventType eventCompleted,
            string remoteServerIpAddress,
            int remoteServerPort,
            string localIpAddress,
            int localPort,
            CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
            }

            var transferSocket =
                new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            EventOccurred?.Invoke(this,
            new ServerEventArgs
            { EventType = ServerEventType.ConnectToRemoteServerStarted });

            var connectResult =
                await transferSocket.ConnectWithTimeoutAsync(
                        remoteServerIpAddress,
                        remoteServerPort,
                        _connectTimeoutMs).ConfigureAwait(false);

            if (connectResult.Failure)
            {
                return connectResult;
            }

            EventOccurred?.Invoke(this,
            new ServerEventArgs
            { EventType = ServerEventType.ConnectToRemoteServerCompleted });

            EventOccurred?.Invoke(this,
            new ServerEventArgs
            {
                EventType = eventStarted,
                RemoteServerIpAddress = remoteServerIpAddress,
                RemoteServerPortNumber = remoteServerPort
            });

            var messageData =
                MessageWrapper.ConstructGenericMessage(requestType, localIpAddress, localPort);

            var messageLength = BitConverter.GetBytes(messageData.Length);

            var sendMessageResult =
                await transferSocket.SendWithTimeoutAsync(
                        messageLength,
                        0,
                        messageLength.Length,
                        0,
                        _sendTimeoutMs).ConfigureAwait(false);

            if (sendMessageResult.Failure)
            {
                return sendMessageResult;
            }

            sendMessageResult =
                await transferSocket.SendWithTimeoutAsync(
                        messageData,
                        0,
                        messageData.Length,
                        0,
                        _sendTimeoutMs).ConfigureAwait(false);

            if (sendMessageResult.Failure)
            {
                return sendMessageResult;
            }

            EventOccurred?.Invoke(this,
            new ServerEventArgs
            { EventType = eventCompleted });

            transferSocket.Shutdown(SocketShutdown.Both);
            transferSocket.Close();

            return Result.Ok();
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

            if (token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
            }

            var transferSocket =
                new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            EventOccurred?.Invoke(this,
            new ServerEventArgs
                { EventType = ServerEventType.ConnectToRemoteServerStarted });

            var connectResult =
                await transferSocket.ConnectWithTimeoutAsync(
                        remoteServerIpAddress,
                        remoteServerPort,
                        _connectTimeoutMs).ConfigureAwait(false);

            if (connectResult.Failure)
            {
                return connectResult;
            }

            EventOccurred?.Invoke(this,
            new ServerEventArgs
                { EventType = ServerEventType.ConnectToRemoteServerCompleted });

            EventOccurred?.Invoke(this,
            new ServerEventArgs
            {
                EventType = ServerEventType.SendTextMessageStarted,
                TextMessage = message,
                RemoteServerIpAddress = remoteServerIpAddress,
                RemoteServerPortNumber = remoteServerPort
            });

            var messageData =
                MessageWrapper.ConstuctTextMessageRequest(message, localIpAddress, localPort);

            var messageLength = BitConverter.GetBytes(messageData.Length);

            var sendMessageResult =
                await transferSocket.SendWithTimeoutAsync(
                        messageLength,
                        0,
                        messageLength.Length,
                        0,
                        _sendTimeoutMs).ConfigureAwait(false);

            if (sendMessageResult.Failure)
            {
                return sendMessageResult;
            }

            sendMessageResult =
                await transferSocket.SendWithTimeoutAsync(
                        messageData,
                        0,
                        messageData.Length,
                        0,
                        _sendTimeoutMs).ConfigureAwait(false);

            if (sendMessageResult.Failure)
            {
                return sendMessageResult;
            }

            EventOccurred?.Invoke(this,
            new ServerEventArgs
                { EventType = ServerEventType.SendTextMessageCompleted });

            transferSocket.Shutdown(SocketShutdown.Both);
            transferSocket.Close();

            return Result.Ok();
        }

        public async Task<Result> SendFileAsync(
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

            if (token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
            }

            _serverSocket =
                new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            EventOccurred?.Invoke(this,
            new ServerEventArgs
                { EventType = ServerEventType.ConnectToRemoteServerStarted });

            var connectResult =
                await _serverSocket.ConnectWithTimeoutAsync(
                        remoteServerIpAddress,
                        remoteServerPort,
                        _connectTimeoutMs).ConfigureAwait(false);

            if (connectResult.Failure)
            {
                return connectResult;
            }

            EventOccurred?.Invoke(this,
            new ServerEventArgs
                { EventType = ServerEventType.ConnectToRemoteServerCompleted });

            var fileSizeBytes = new FileInfo(localFilePath).Length;
            EventOccurred?.Invoke(this,
            new ServerEventArgs
            {
                EventType = ServerEventType.SendOutboundFileTransferInfoStarted,
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
                    _localIpAddress,
                    _localPort,
                    remoteFolderPath);

            var messageLength = BitConverter.GetBytes(messageData.Length);

            var sendMessageResult =
                await _serverSocket.SendWithTimeoutAsync(
                    messageLength,
                        0,
                        messageLength.Length,
                        0,
                        _sendTimeoutMs).ConfigureAwait(false);

            if (sendMessageResult.Failure)
            {
                return sendMessageResult;
            }

            sendMessageResult =
                await _serverSocket.SendWithTimeoutAsync(
                        messageData,
                        0,
                        messageData.Length,
                        0,
                        _sendTimeoutMs).ConfigureAwait(false);

            if (sendMessageResult.Failure)
            {
                return sendMessageResult;
            }

            EventOccurred?.Invoke(this,
            new ServerEventArgs
                { EventType = ServerEventType.SendOutboundFileTransferInfoCompleted });

            var sendFileTask =
                Task.Run(() => 
                    SendFileBytesAsync(localFilePath, fileSizeBytes, token),
                    token);

            return Result.Ok();
        }

        async Task<Result> SendFileBytesAsync(
            string localFilePath,
            long fileSizeBytes,
            CancellationToken token)
        {
            EventOccurred?.Invoke(this,
            new ServerEventArgs
                { EventType = ServerEventType.SendFileBytesStarted });

            var totalBytesRemaining = fileSizeBytes;
            var fileChunkSentCount = 0;
            var socketSendCount = 0;
            _fileTransferIsStalled = false;

            using (var file = File.OpenRead(localFilePath))
            {
                while (totalBytesRemaining > 0)
                {
                    if (token.IsCancellationRequested)
                    {
                        token.ThrowIfCancellationRequested();
                    }

                    var fileChunkSize = (int) Math.Min(_bufferSize, totalBytesRemaining);
                    _buffer = new byte[fileChunkSize];

                    var numberOfBytesToSend = file.Read(_buffer, 0, fileChunkSize);
                    totalBytesRemaining -= numberOfBytesToSend;

                    var offset = 0;
                    while (numberOfBytesToSend > 0)
                    {
                        SendFileChunk(offset, fileChunkSize);
                        _sendSync.WaitOne();

                        if (_fileTransferIsStalled)
                        {
                            return Result.Fail(FileTransferStalledErrorMessage);
                        }

                        numberOfBytesToSend -= _lastBytesSentCount;
                        offset += _lastBytesSentCount;
                        socketSendCount++;
                    }
                    
                    fileChunkSentCount++;

                    if (fileSizeBytes > (10 * 1024)) continue;
                    EventOccurred?.Invoke(this,
                    new ServerEventArgs
                    {
                        EventType = ServerEventType.SentFileChunkToClient,
                        FileSizeInBytes = fileSizeBytes,
                        CurrentFileBytesSent = fileChunkSize,
                        FileBytesRemaining = totalBytesRemaining,
                        FileChunkSentCount = fileChunkSentCount,
                        SocketSendCount = socketSendCount
                    });
                }

                EventOccurred?.Invoke(this,
                new ServerEventArgs
                    { EventType = ServerEventType.SendFileBytesCompleted });

                var receiveConfirationMessageResult =
                    await ReceiveConfirmationAsync(_serverSocket).ConfigureAwait(false);

                if (receiveConfirationMessageResult.Failure)
                {
                    return receiveConfirationMessageResult;
                }

                _serverSocket.Shutdown(SocketShutdown.Both);
                _serverSocket.Close();
                
                return Result.Ok();
            }
        }

        void SendFileChunk(int offset, int fileBytesCount)
        {   
            _serverSocket.BeginSend(
                _buffer,
                offset,
                fileBytesCount,
                SocketFlags.None,
                SentFileChunk,
                null);
        }

        void SentFileChunk(IAsyncResult ar)
        {
            _lastBytesSentCount = _serverSocket.EndSend(ar);
            _sendSync.Set();
        }

        async Task<Result> ReceiveConfirmationAsync(Socket transferSocket)
        {
            var buffer = new byte[_bufferSize];
            Result<int> receiveMessageResult;
            int bytesReceived;

            EventOccurred?.Invoke(this,
            new ServerEventArgs
            { EventType = ServerEventType.ReceiveConfirmationMessageStarted });

            try
            {
                receiveMessageResult =
                    await transferSocket.ReceiveAsync(
                        buffer,
                        0,
                        _bufferSize,
                        0).ConfigureAwait(false);

                bytesReceived = receiveMessageResult.Value;
            }
            catch (SocketException ex)
            {
                return Result.Fail($"{ex.Message} ({ex.GetType()} raised in method TplSocketServer.ReceiveConfirmationAsync)");
            }
            catch (TimeoutException ex)
            {
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
            new ServerEventArgs
            {
                EventType = ServerEventType.ReceiveConfirmationMessageCompleted,
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
            if (token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
            }

            var transferSocket =
                new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            EventOccurred?.Invoke(this,
            new ServerEventArgs
                { EventType = ServerEventType.ConnectToRemoteServerStarted });

            var connectResult =
                await transferSocket.ConnectWithTimeoutAsync(
                        remoteServerIpAddress,
                        remoteServerPort,
                        _connectTimeoutMs).ConfigureAwait(false);

            if (connectResult.Failure)
            {
                return connectResult;
            }

            EventOccurred?.Invoke(this,
            new ServerEventArgs
                { EventType = ServerEventType.ConnectToRemoteServerCompleted });

            EventOccurred?.Invoke(this,
            new ServerEventArgs
            {
                EventType = ServerEventType.SendInboundFileTransferInfoStarted,
                RemoteServerIpAddress = remoteServerIpAddress,
                RemoteServerPortNumber = remoteServerPort,
                RemoteFolder = Path.GetDirectoryName(remoteFilePath),
                FileName = Path.GetFileName(remoteFilePath),
                LocalFolder = localFolderPath,
            });

            var messageData =
                MessageWrapper.ConstructInboundFileTransferRequest(
                    remoteFilePath,
                    localIpAddress,
                    localPort,
                    localFolderPath);

            var messageLength = BitConverter.GetBytes(messageData.Length);

            var sendMessageResult =
                await transferSocket.SendWithTimeoutAsync(
                        messageLength,
                        0,
                        messageLength.Length,
                        0,
                        _sendTimeoutMs).ConfigureAwait(false);

            if (sendMessageResult.Failure)
            {
                return sendMessageResult;
            }

            sendMessageResult =
                await transferSocket.SendWithTimeoutAsync(
                        messageData,
                        0,
                        messageData.Length,
                        0,
                        _sendTimeoutMs).ConfigureAwait(false);

            if (sendMessageResult.Failure)
            {
                return sendMessageResult;
            }

            EventOccurred?.Invoke(this,
            new ServerEventArgs
                { EventType = ServerEventType.SendInboundFileTransferInfoCompleted });

            transferSocket.Shutdown(SocketShutdown.Both);
            transferSocket.Close();

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
            if (token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
            }

            var transferSocket =
                new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            EventOccurred?.Invoke(this,
            new ServerEventArgs
                { EventType = ServerEventType.ConnectToRemoteServerStarted });

            var connectResult =
                await transferSocket.ConnectWithTimeoutAsync(
                        remoteServerIpAddress,
                        remoteServerPort,
                        _connectTimeoutMs).ConfigureAwait(false);

            if (connectResult.Failure)
            {
                return connectResult;
            }

            EventOccurred?.Invoke(this,
            new ServerEventArgs
                { EventType = ServerEventType.ConnectToRemoteServerCompleted });

            EventOccurred?.Invoke(this,
            new ServerEventArgs
            {
                EventType = ServerEventType.SendFileListRequestStarted,
                RemoteServerIpAddress = remoteServerIpAddress,
                RemoteServerPortNumber = remoteServerPort,
                RemoteFolder = targetFolder
            });

            var messageData =
                MessageWrapper.ConstructFileListRequest(localIpAddress, localPort, targetFolder);

            var messageLength = BitConverter.GetBytes(messageData.Length);

            var sendMessageResult =
                await transferSocket.SendWithTimeoutAsync(
                        messageLength,
                        0,
                        messageLength.Length,
                        0,
                        _sendTimeoutMs).ConfigureAwait(false);

            if (sendMessageResult.Failure)
            {
                return sendMessageResult;
            }

            sendMessageResult =
                await transferSocket.SendWithTimeoutAsync(
                        messageData,
                        0,
                        messageData.Length,
                        0,
                        _sendTimeoutMs).ConfigureAwait(false);

            if (sendMessageResult.Failure)
            {
                return sendMessageResult;
            }

            EventOccurred?.Invoke(this,
            new ServerEventArgs
                { EventType = ServerEventType.SendFileListRequestCompleted });

            transferSocket.Shutdown(SocketShutdown.Both);
            transferSocket.Close();

            return Result.Ok();
        }

        public Task<Result> NotifyClientDataIsNotBeingReceived(
            string remoteServerIpAddress,
            int remoteServerPort,
            string localIpAddress,
            int localPort,
            CancellationToken token)
        {
            _fileTransferIsStalled = true;

            return
                SendGenericMessageToClient(
                    RequestType.DataIsNoLongerBeingReceived,
                    ServerEventType.NotifyClientDataIsNoLongerBeingReceivedStarted,
                    ServerEventType.NotifyClientDataIsNoLongerBeingReceivedCompleted,
                    remoteServerIpAddress,
                    remoteServerPort,
                    localIpAddress,
                    localPort,
                    token);
        }

        public Task<Result> RequestTransferFolderPath(
            string remoteServerIpAddress,
            int remoteServerPort,
            string localIpAddress,
            int localPort,
            CancellationToken token)
        {
            return
                SendGenericMessageToClient(
                    RequestType.TransferFolderPathRequest,
                    ServerEventType.SendTransferFolderRequestStarted,
                    ServerEventType.SendTransferFolderRequestCompleted,
                    remoteServerIpAddress,
                    remoteServerPort,
                    localIpAddress,
                    localPort,
                    token);
        }

        public Task<Result> RequestPublicIp(
            string remoteServerIpAddress,
            int remoteServerPort,
            string localIpAddress,
            int localPort,
            CancellationToken token)
        {
            return
                SendGenericMessageToClient(
                    RequestType.PublicIpAddressRequest,
                    ServerEventType.SendPublicIpRequestStarted,
                    ServerEventType.SendPublicIpRequestCompleted,
                    remoteServerIpAddress,
                    remoteServerPort,
                    localIpAddress,
                    localPort,
                    token);
        }

        public Result CloseListenSocket()
        {
            EventOccurred?.Invoke(this,
            new ServerEventArgs
                { EventType = ServerEventType.ShutdownListenSocketStarted });

            try
            {
                _listenSocket.Shutdown(SocketShutdown.Both);
                _listenSocket.Close();
            }
            catch (SocketException ex)
            {
                EventOccurred?.Invoke(this,
                new ServerEventArgs
                    { EventType = ServerEventType.ShutdownListenSocketCompleted });

                return Result.Ok($"{ex.Message} ({ex.GetType()} raised in method TplSocketServer.CloseListenSocket)");
            }

            EventOccurred?.Invoke(this,
            new ServerEventArgs
                { EventType = ServerEventType.ShutdownListenSocketCompleted });

            return Result.Ok();
        }
    }
}