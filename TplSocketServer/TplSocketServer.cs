namespace TplSocketServer
{
    using AaronLuna.Common.IO;
    using AaronLuna.Common.Result;

    using System;
    using System.IO;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    public class TplSocketServer
    {
        const string ConfirmationMessage = "handshake";
        const int OneSecondInMilliseconds = 1000;

        readonly int _maxConnections;
        readonly int _bufferSize;

        readonly int _connectTimeoutMs;
        readonly int _receiveTimeoutMs;
        readonly int _sendTimeoutMs;

        Socket _listenSocket;
        Socket _transferSocket;

        public TplSocketServer()
        {
            _maxConnections = 5;
            _bufferSize = 1024 * 8;
            _connectTimeoutMs = 5000;
            _receiveTimeoutMs = 5000;
            _sendTimeoutMs = 5000;

            _listenSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        }

        public TplSocketServer(int maxConnections, int bufferSize, int connectTimeoutMs, int receiveTimeoutMs, int sendTimeoutMs)
        {
            _maxConnections = maxConnections;
            _bufferSize = bufferSize;
            _connectTimeoutMs = connectTimeoutMs;
            _receiveTimeoutMs = receiveTimeoutMs;
            _sendTimeoutMs = sendTimeoutMs;

            _listenSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        }
        
        public event ServerEventDelegate EventOccurred;

        public async Task<Result> HandleIncomingConnectionsAsync(IPAddress ipAddress, int localPort, CancellationToken token)
        {
            return (await Task.Factory.StartNew(() => Listen(ipAddress, localPort), token).ConfigureAwait(false))
                .OnSuccess(() => WaitForConnectionsAsync(token));
        }

        private Result Listen(IPAddress ipAddress, int localPort)
        { 
            EventOccurred?.Invoke(new ServerEventInfo { EventType = ServerEventType.ListenOnLocalPortStarted });
            
            var ipEndPoint = new IPEndPoint(ipAddress, localPort);            
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

            EventOccurred?.Invoke(new ServerEventInfo { EventType = ServerEventType.ListenOnLocalPortCompleted });

            return Result.Ok();
        }

        private async Task<Result> WaitForConnectionsAsync(CancellationToken token)
        {
            // Main loop. Server handles incoming connections until encountering an error
            while (true)
            {
                if (token.IsCancellationRequested)
                {
                    token.ThrowIfCancellationRequested();
                }

                var acceptConnectionResult = await AcceptNextConnection(token).ConfigureAwait(false);

                if (acceptConnectionResult.Success)
                {
                    var requestResult = await ProcessRequestAsync(token).ConfigureAwait(false);

                    if (requestResult.Failure)
                    {
                        EventOccurred?.Invoke(new ServerEventInfo
                        {
                            EventType = ServerEventType.ErrorOccurred,
                            ErrorMessage = requestResult.Error
                        });

                        return requestResult;
                    }
                }
            }
        }

        private async Task<Result> AcceptNextConnection(CancellationToken token)
        {
            EventOccurred?.Invoke(new ServerEventInfo { EventType = ServerEventType.ConnectionAttemptStarted });

            var acceptResult = await _listenSocket.AcceptTaskAsync().ConfigureAwait(false);
            if (acceptResult.Failure)
            {
                return acceptResult;
            }

            if (token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
            }

            _transferSocket = acceptResult.Value;

            EventOccurred?.Invoke(new ServerEventInfo { EventType = ServerEventType.ConnectionAttemptCompleted });

            return Result.Ok();
        }

        private async Task<Result> ProcessRequestAsync(CancellationToken token)
        {
            EventOccurred?.Invoke(new ServerEventInfo { EventType = ServerEventType.DetermineTransferTypeStarted });
            
            if (token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
            }

            var buffer = new byte[_bufferSize];

            var determineTransferTypeResult = await DetermineTransferTypeAsync(buffer).ConfigureAwait(false);
            if (determineTransferTypeResult.Failure)
            {
                return determineTransferTypeResult;
            }

            var transferType = determineTransferTypeResult.Value;
            switch (transferType)
            {
                case TransferType.TextMessage:

                    EventOccurred?.Invoke(
                        new ServerEventInfo
                        {
                            EventType = ServerEventType.DetermineTransferTypeCompleted,
                            TransferType = TransferType.TextMessage
                        });

                    return ReceiveTextMessage(buffer, token);

                case TransferType.InboundFileTransfer:

                    EventOccurred?.Invoke(
                        new ServerEventInfo
                        {
                            EventType = ServerEventType.DetermineTransferTypeCompleted,
                            TransferType = TransferType.InboundFileTransfer
                        });

                    return await InboundFileTransferAsync(buffer, token).ConfigureAwait(false);

                case TransferType.OutboundFileTransfer:

                    EventOccurred?.Invoke(
                        new ServerEventInfo
                        {
                            EventType = ServerEventType.DetermineTransferTypeCompleted,
                            TransferType = TransferType.OutboundFileTransfer
                        });

                    return await OutboundFileTransferAsync(buffer, token).ConfigureAwait(false);

                default:

                    var error = "Unable to determine transfer type, value of " + "'" + transferType + "' is invalid.";
                    return Result.Fail(error);
            }
        }

        private async Task<Result<TransferType>> DetermineTransferTypeAsync(byte[] buffer)
        {
            Result<int> receiveResult;
            int bytesReceived;

            try
            {
                receiveResult = 
                    await _transferSocket.ReceiveWithTimeoutAsync(buffer, 0, _bufferSize, 0, _receiveTimeoutMs)
                        .ConfigureAwait(false);

                bytesReceived = receiveResult.Value;
            }
            catch (SocketException ex)
            {
                return Result.Fail<TransferType>($"{ex.Message} ({ex.GetType()} raised in method TplSocketServer.DetermineTransferTypeAsync)");
            }
            catch (TimeoutException ex)
            {
                return Result.Fail<TransferType>($"{ex.Message} ({ex.GetType()} raised in method TplSocketServer.DetermineTransferTypeAsync)");
            }

            if (receiveResult.Failure)
            {
                return Result.Fail<TransferType>(receiveResult.Error);
            }

            if (bytesReceived == 0)
            {
                return Result.Fail<TransferType>("Error reading request from client, no data was received");
            }

            var transferType = MessageUnwrapper.DetermineTransferType(buffer).ToString();
            var transferTypeEnum = (TransferType)Enum.Parse(typeof(TransferType), transferType);

            return Result.Ok(transferTypeEnum);
        }

        private Result ReceiveTextMessage(byte[] buffer, CancellationToken token)
        {
            EventOccurred?.Invoke(new ServerEventInfo { EventType = ServerEventType.ReceiveTextMessageStarted });

            var(message, 
                remoteIpAddress, 
                remotePortNumber) = MessageUnwrapper.ReadTextMessageRequest(buffer);

            if (token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
            }

            EventOccurred?.Invoke(
                new ServerEventInfo
                {
                    EventType = ServerEventType.ReceiveTextMessageCompleted,
                    TextMessage = message,
                    RemoteServerIpAddress = remoteIpAddress,
                    RemoteServerPortNumber = remotePortNumber
                });

            return Result.Ok();
        }

        private async Task<Result> InboundFileTransferAsync(byte[] buffer, CancellationToken token)
        {
            EventOccurred?.Invoke(
                new ServerEventInfo { EventType = ServerEventType.ReceiveInboundFileTransferInfoStarted });

            var(localFilePath, 
                fileSizeBytes) = MessageUnwrapper.ReadInboundFileTransferRequest(buffer);

            if (token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
            }

            var deleteFileResult = FileHelper.DeleteFileIfAlreadyExists(localFilePath);
            if (deleteFileResult.Failure)
            {
                return deleteFileResult;
            }

            EventOccurred?.Invoke(
                new ServerEventInfo
                {
                    EventType = ServerEventType.ReceiveInboundFileTransferInfoCompleted,
                    LocalFolder = Path.GetDirectoryName(localFilePath),
                    FileName = Path.GetFileName(localFilePath),
                    FileSizeInBytes = fileSizeBytes
                });

            var startTime = DateTime.Now;

            EventOccurred?.Invoke(new ServerEventInfo
            {
                EventType = ServerEventType.ReceiveFileBytesStarted,
                FileTransferStartTime = startTime            
            });

            var receiveFileResult = 
                await ReceiveFileAsync(localFilePath, fileSizeBytes, buffer, 0, _bufferSize, 0, _receiveTimeoutMs, token)
                    .ConfigureAwait(false);

            if (receiveFileResult.Failure)
            {
                return receiveFileResult;
            }

            EventOccurred?.Invoke(new ServerEventInfo
            {
                EventType = ServerEventType.ReceiveFileBytesCompleted,
                FileTransferStartTime = startTime,
                FileTransferCompleteTime = DateTime.Now,
                FileSizeInBytes = fileSizeBytes
            });

            //TODO: This is a hack to separate the file read and handshake steps to ensure that all data is read correcty by the client server. I know how to fix by keeping track of the buffer between steps.
            await Task.Delay(OneSecondInMilliseconds);

            EventOccurred?.Invoke(
                new ServerEventInfo
                {
                    EventType = ServerEventType.SendConfirmationMessageStarted,
                    ConfirmationMessage = ConfirmationMessage
                });

            var confirmationMessageData = Encoding.ASCII.GetBytes(ConfirmationMessage);

            var sendConfirmatinMessageResult = 
                await _transferSocket.SendWithTimeoutAsync(confirmationMessageData, 0, confirmationMessageData.Length, 0, _sendTimeoutMs)
                    .ConfigureAwait(false);

            if (sendConfirmatinMessageResult.Failure)
            {
                return sendConfirmatinMessageResult;
            }

            EventOccurred?.Invoke(new ServerEventInfo { EventType = ServerEventType.SendConfirmationMessageCompleted });

            return Result.Ok();
        }

        private async Task<Result> ReceiveFileAsync(
            string localFilePath,
            long fileSizeInBytes,
            byte[] buffer,
            int offset,
            int size,
            SocketFlags socketFlags,
            int receiveTimout,
            CancellationToken token)
        {
            long totalBytesReceived = 0;
            float percentComplete = 0;
            int receiveCount = 0;

            // Read file bytes from transfer socket until 
            //      1. the entire file has been received OR 
            //      2. Data is no longer being received OR
            //      3, Transfer is cancelled by server receiving the file
            while (true)
            {
                if (totalBytesReceived == fileSizeInBytes)
                {
                    percentComplete = 1;
                    EventOccurred?.Invoke(
                        new ServerEventInfo
                        {
                            EventType = ServerEventType.FileTransferProgress,
                            PercentComplete = percentComplete
                        });

                    return Result.Ok();
                }

                if (token.IsCancellationRequested)
                {
                    token.ThrowIfCancellationRequested();
                }

                int bytesReceived;
                try
                {
                    var receiveBytesResult = await _transferSocket
                                                 .ReceiveWithTimeoutAsync(
                                                     buffer,
                                                     offset,
                                                     size,
                                                     socketFlags,
                                                     receiveTimout).ConfigureAwait(false);

                    bytesReceived = receiveBytesResult.Value;
                }
                catch (SocketException ex)
                {
                    return Result.Fail($"{ex.Message} ({ex.GetType()} raised in method TplSocketServer.ReceiveFileAsync)");
                }
                catch (TimeoutException ex)
                {
                    return Result.Fail($"{ex.Message} ({ex.GetType()} raised in method TplSocketServer.ReceiveFileAsync)");
                }

                if (bytesReceived == 0)
                {
                    return Result.Fail("Socket is no longer receiving data, must abort file transfer");
                }

                totalBytesReceived += bytesReceived;
                var writeBytesResult = FileHelper.WriteBytesToFile(localFilePath, buffer, bytesReceived);

                if (writeBytesResult.Failure)
                {
                    return writeBytesResult;
                }

                // THese two lines and the event raised below are useful when debugging socket errors
                receiveCount++;
                long bytesRemaining = fileSizeInBytes - totalBytesReceived;

                //EventOccurred?.Invoke(new ServerEventInfo
                //{
                //    EventType = ServerEventType.ReceivedDataFromSocket,
                //    ReceiveBytesCount = receiveBytesCount,
                //    CurrentBytesReceivedFromSocket = bytesReceived,
                //    TotalBytesReceivedFromSocket = totalBytesReceived,
                //    FileSizeInBytes = fileSizeInBytes,
                //    BytesRemainingInFile = bytesRemaining
                //});

                var checkPercentComplete = totalBytesReceived / (float)fileSizeInBytes;
                var changeSinceLastUpdate = checkPercentComplete - percentComplete;

                // Report progress only if at least 1% of file has been received since the last update
                if (changeSinceLastUpdate > (float).01)
                {
                    percentComplete = checkPercentComplete;
                    EventOccurred?.Invoke(
                        new ServerEventInfo
                        {
                            EventType = ServerEventType.FileTransferProgress,
                            PercentComplete = percentComplete
                        });
                }
            }           
        }

        private async Task<Result> OutboundFileTransferAsync(byte[] buffer, CancellationToken token)
        {
            EventOccurred?.Invoke(
                new ServerEventInfo { EventType = ServerEventType.ReceiveOutboundFileTransferInfoStarted });

            var(requestedFilePath, 
                remoteServerIpAddress, 
                remoteServerPort, 
                remoteFolderPath) = MessageUnwrapper.ReadOutboundFileTransferRequest(buffer);

            EventOccurred?.Invoke(
                new ServerEventInfo
                {
                    EventType = ServerEventType.ReceiveOutboundFileTransferInfoCompleted,
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
                SendFileAsync(remoteServerIpAddress, remoteServerPort, requestedFilePath, remoteFolderPath, token)
                    .ConfigureAwait(false);
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

            var transferSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            EventOccurred?.Invoke(new ServerEventInfo { EventType = ServerEventType.ConnectToRemoteServerStarted });

            var connectResult =
                await transferSocket.ConnectWithTimeoutAsync(remoteServerIpAddress, remoteServerPort, _connectTimeoutMs)
                    .ConfigureAwait(false);

            if (connectResult.Failure)
            {
                return connectResult;
            }

            EventOccurred?.Invoke(new ServerEventInfo { EventType = ServerEventType.ConnectToRemoteServerCompleted });
            EventOccurred?.Invoke(
                new ServerEventInfo
                {
                    EventType = ServerEventType.SendTextMessageStarted,
                    TextMessage = message,
                    RemoteServerIpAddress = remoteServerIpAddress,
                    RemoteServerPortNumber = remoteServerPort
                });

            var messageWrapperAndData = MessageWrapper.ConstuctTextMessageRequest(message, localIpAddress, localPort);

            var sendMessageResult = 
                await transferSocket.SendWithTimeoutAsync(messageWrapperAndData, 0, messageWrapperAndData.Length, 0, _sendTimeoutMs)
                    .ConfigureAwait(false);

            if (sendMessageResult.Failure)
            {
                return sendMessageResult;
            }

            EventOccurred?.Invoke(new ServerEventInfo { EventType = ServerEventType.SendTextMessageCompleted });

            transferSocket.Shutdown(SocketShutdown.Both);
            transferSocket.Close();

            return Result.Ok();
        }
        
        public async Task<Result> SendFileAsync(string remoteServerIpAddress, int remoteServerPort, string localFilePath, string remoteFolderPath, CancellationToken token)
        {
            if (!File.Exists(localFilePath))
            {
                return Result.Fail("File does not exist: " + localFilePath);
            }

            if (token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
            }

            var transferSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            EventOccurred?.Invoke(new ServerEventInfo { EventType = ServerEventType.ConnectToRemoteServerStarted });

            var connectResult =
                await transferSocket.ConnectWithTimeoutAsync(remoteServerIpAddress, remoteServerPort, _connectTimeoutMs)
                    .ConfigureAwait(false);

            if (connectResult.Failure)
            {
                return connectResult;
            }

            EventOccurred?.Invoke(new ServerEventInfo { EventType = ServerEventType.ConnectToRemoteServerCompleted });

            var fileSizeBytes = new FileInfo(localFilePath).Length;

            var messageWrapper = 
                MessageWrapper.ConstructOutboundFileTransferRequest(localFilePath, fileSizeBytes, remoteFolderPath);

            EventOccurred?.Invoke(
                new ServerEventInfo
                    {
                        EventType = ServerEventType.SendOutboundFileTransferInfoStarted,
                        LocalFolder = Path.GetDirectoryName(localFilePath),
                        FileName = Path.GetFileName(localFilePath),
                        FileSizeInBytes = fileSizeBytes,
                        RemoteServerIpAddress = remoteServerIpAddress,
                        RemoteServerPortNumber = remoteServerPort,
                        RemoteFolder = remoteFolderPath
                    });

            var sendRequest = 
                await transferSocket.SendWithTimeoutAsync(messageWrapper, 0, messageWrapper.Length, 0, _sendTimeoutMs)
                    .ConfigureAwait(false);

            if (sendRequest.Failure)
            {
                return sendRequest;
            }

            EventOccurred?.Invoke(new ServerEventInfo { EventType = ServerEventType.SendOutboundFileTransferInfoCompleted });

            //TODO: This is a hack to separate the transfer request and file transfer steps to ensure that all data is read correcty by the client server. I know how to fix by keeping track of the buffer between steps.
            await Task.Delay(OneSecondInMilliseconds);

            EventOccurred?.Invoke(new ServerEventInfo { EventType = ServerEventType.SendFileBytesStarted });

            var sendFileResult = await transferSocket.SendFileAsync(localFilePath).ConfigureAwait(false);
            if (sendFileResult.Failure)
            {
                return sendFileResult;
            }

            EventOccurred?.Invoke(new ServerEventInfo { EventType = ServerEventType.SendFileBytesCompleted });

            var receiveConfirationMessageResult = await ReceiveConfirmationAsync(transferSocket).ConfigureAwait(false);
            if (receiveConfirationMessageResult.Failure)
            {
                return receiveConfirationMessageResult;
            }
            
            transferSocket.Shutdown(SocketShutdown.Both);
            transferSocket.Close();

            return Result.Ok();
        }

        async Task<Result> ReceiveConfirmationAsync(Socket transferSocket)
        {
            var buffer = new byte[_bufferSize];
            Result<int> receiveMessageResult;
            int bytesReceived;

            EventOccurred?.Invoke(new ServerEventInfo { EventType = ServerEventType.ReceiveConfirmationMessageStarted });

            try
            {
                receiveMessageResult = await transferSocket.ReceiveAsync(buffer, 0, _bufferSize, 0).ConfigureAwait(false);
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

            EventOccurred?.Invoke(
                new ServerEventInfo
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
            if (!_listenSocket.IsBound)
            {
                return Result.Fail("Server's listening port is unbound, cannot accept inbound file transfers");
            }

            if (token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
            }

            var transferSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            EventOccurred?.Invoke(new ServerEventInfo { EventType = ServerEventType.ConnectToRemoteServerStarted });

            var connectResult = 
                await transferSocket.ConnectWithTimeoutAsync(remoteServerIpAddress, remoteServerPort, _connectTimeoutMs)
                    .ConfigureAwait(false);

            if (connectResult.Failure)
            {
                return connectResult;
            }

            EventOccurred?.Invoke(new ServerEventInfo { EventType = ServerEventType.ConnectToRemoteServerCompleted });

            var messageHeaderData = 
                MessageWrapper.ConstructInboundFileTransferRequest(
                    remoteFilePath,
                    localIpAddress,
                    localPort,
                    localFolderPath);

            EventOccurred?.Invoke(
                new ServerEventInfo
                    {
                        EventType = ServerEventType.SendInboundFileTransferInfoStarted,
                        RemoteServerIpAddress = remoteServerIpAddress,
                        RemoteServerPortNumber = remoteServerPort,
                        RemoteFolder = Path.GetDirectoryName(remoteFilePath),
                        FileName = Path.GetFileName(remoteFilePath),
                        LocalFolder = localFolderPath,
                    });

            var requestResult = 
                await transferSocket.SendWithTimeoutAsync(messageHeaderData, 0, messageHeaderData.Length, 0, _sendTimeoutMs)
                    .ConfigureAwait(false);

            if (requestResult.Failure)
            {
                return requestResult;
            }

            EventOccurred?.Invoke(new ServerEventInfo { EventType = ServerEventType.SendInboundFileTransferInfoCompleted });

            transferSocket.Shutdown(SocketShutdown.Both);
            transferSocket.Close();
            
            return Result.Ok();
        }

        public Result CloseListenSocket()
        {
            EventOccurred?.Invoke(new ServerEventInfo { EventType = ServerEventType.ShutdownListenSocketStarted });

            try
            {
                _listenSocket.Shutdown(SocketShutdown.Both);
                _listenSocket.Close();
            }
            catch (SocketException ex)
            {
                EventOccurred?.Invoke(new ServerEventInfo { EventType = ServerEventType.ShutdownListenSocketCompleted });
                return Result.Ok($"{ex.Message} ({ex.GetType()} raised in method TplSocketServer.CloseListenSocket)");
            }

            EventOccurred?.Invoke(new ServerEventInfo { EventType = ServerEventType.ShutdownListenSocketCompleted });
            return Result.Ok();
        }
    }
}