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
        private const string ConfirmationMessage = "handshake";
        private const string FileAlreadyExists = "A file with the same name already exists in the download folder, please rename or remove this file in order to proceed.";
        private const string EmptyTransferFolderErrorMessage = "Currently there are no files available in transfer folder";

        private readonly int _maxConnections;
        private readonly int _bufferSize;
        private readonly int _connectTimeoutMs;
        private readonly int _receiveTimeoutMs;
        private readonly int _sendTimeoutMs;

        private string _localIpAddress;
        private int _localPort;
        private readonly Socket _listenSocket;
        private Socket _clientSocket;
        private byte[] _buffer;
        private List<byte> _unreadBytes;
        private int _lastBytesReceivedCount;

        public TplSocketServer()
        {
            CidrMask = "192.168.2.0/24";

            _localIpAddress = GetLocalIpAddress();
            TransferFolderPath = $"{Directory.GetCurrentDirectory()}{Path.DirectorySeparatorChar}transfer";

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

            _maxConnections = appSettings.SocketSettings.MaxNumberOfConections;
            _bufferSize = appSettings.SocketSettings.BufferSize;
            _connectTimeoutMs = appSettings.SocketSettings.ConnectTimeoutMs;
            _receiveTimeoutMs = appSettings.SocketSettings.ReceiveTimeoutMs;
            _sendTimeoutMs = appSettings.SocketSettings.SendTimeoutMs;

            _listenSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        }

        public string CidrMask { get; set; }
        public string TransferFolderPath { get; set; }

        public event ServerEventDelegate EventOccurred;

        public async Task<Result> HandleIncomingConnectionsAsync(string localIpAddress, int localPort,
            CancellationToken token)
        {
            _localIpAddress = localIpAddress;
            _localPort = localPort;

            return (await Task.Factory.StartNew(() => Listen(localIpAddress, localPort), token).ConfigureAwait(false))
                .OnSuccess(() => WaitForConnectionsAsync(token));
        }

        public async Task<Result> HandleIncomingConnectionsAsync(int localPort, CancellationToken token)
        {
            _localPort = localPort;

            return (await Task.Factory.StartNew(() => Listen(string.Empty, _localPort), token).ConfigureAwait(false))
                .OnSuccess(() => WaitForConnectionsAsync(token));
        }

        private Result Listen(string localIpAdress, int localPort)
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

            EventOccurred?.Invoke(new ServerEventInfo { EventType = ServerEventType.ListenOnLocalPortStarted });

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

            EventOccurred?.Invoke(new ServerEventInfo { EventType = ServerEventType.ListenOnLocalPortCompleted });

            return Result.Ok();
        }

        private string GetLocalIpAddress()
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

        private async Task<Result> WaitForConnectionsAsync(CancellationToken token)
        {
            // Main loop. Server handles incoming connections until encountering an error
            while (true)
            {
                if (token.IsCancellationRequested)
                {
                    token.ThrowIfCancellationRequested();
                }

                EventOccurred?.Invoke(new ServerEventInfo { EventType = ServerEventType.AcceptConnectionAttemptStarted });

                var acceptResult = await _listenSocket.AcceptTaskAsync().ConfigureAwait(false);
                if (acceptResult.Failure)
                {
                    return acceptResult;
                }

                _clientSocket = acceptResult.Value;
                EventOccurred?.Invoke(new ServerEventInfo { EventType = ServerEventType.AcceptConnectionAttemptCompleted });

                var clientRequest = await HandleClientRequestAsync(token).ConfigureAwait(false);
                if (clientRequest.Success) continue;

                EventOccurred?.Invoke(
                    new ServerEventInfo
                    {
                        EventType = ServerEventType.ErrorOccurred,
                        ErrorMessage = clientRequest.Error
                    });
            }
        }

        private async Task<Result> HandleClientRequestAsync(CancellationToken token)
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

        private async Task<Result> ReadIncomingMessageAsync(CancellationToken token)
        {
            if (token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
            }

            _buffer = new byte[_bufferSize];
            _unreadBytes = new List<byte>();

            EventOccurred?.Invoke(new ServerEventInfo { EventType = ServerEventType.DetermineMessageLengthStarted });

            var determineMessageLengthResult = await DetermineMessageLengthAsync().ConfigureAwait(false);
            if (determineMessageLengthResult.Failure)
            {
                return determineMessageLengthResult;
            }

            var messageLength = determineMessageLengthResult.Value;

            EventOccurred?.Invoke(
                new ServerEventInfo
                {
                    EventType = ServerEventType.DetermineMessageLengthCompleted,
                    MessageLength = messageLength,
                    UnreadByteCount = _unreadBytes.Count
                });

            EventOccurred?.Invoke(new ServerEventInfo { EventType = ServerEventType.ReceiveAllMessageBytesStarted });

            var receiveMessageResult = await ReceiveAllMessageBytesAsync(messageLength).ConfigureAwait(false);
            if (receiveMessageResult.Failure)
            {
                return receiveMessageResult;
            }

            var messageData = receiveMessageResult.Value;

            EventOccurred?.Invoke(
                new ServerEventInfo
                {
                    EventType = ServerEventType.ReceiveAllMessageBytesCompleted,
                    UnreadByteCount = _unreadBytes.Count
                });

            return await ProcessRequestAsync(messageData, token).ConfigureAwait(false);
        }

        private async Task<Result<int>> ReadFromSocketAsync()
        {
            Result<int> receiveResult;
            int bytesReceived;

            try
            {
                receiveResult =
                    await _clientSocket.ReceiveWithTimeoutAsync(_buffer, 0, _bufferSize, 0, _receiveTimeoutMs)
                        .ConfigureAwait(false);

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

            if (bytesReceived == 0)
            {
                return Result.Fail<int>("Error reading request from client, no data was received");
            }

            return Result.Ok(bytesReceived);
        }

        private async Task<Result<int>> DetermineMessageLengthAsync()
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
            }

            var messageLength = MessageUnwrapper.ReadInt32(_buffer);

            return Result.Ok(messageLength);
        }

        private async Task<Result<byte[]>> ReceiveAllMessageBytesAsync(int messageLength)
        {
            var messageData = new List<byte>();
            if (_unreadBytes.Count > 0)
            {
                var savedBytes = new byte[_unreadBytes.Count];
                _unreadBytes.CopyTo(0, savedBytes, 0, _unreadBytes.Count);
                messageData.AddRange(savedBytes.ToList());
            }

            var receiveCount = 0;
            var totalBytesReceieved = 0;
            _unreadBytes = new List<byte>();

            while (messageData.Count < messageLength)
            {
                var readFromSocketResult = await ReadFromSocketAsync().ConfigureAwait(false);
                if (readFromSocketResult.Failure)
                {
                    return Result.Fail<byte[]>(readFromSocketResult.Error);
                }

                _lastBytesReceivedCount = readFromSocketResult.Value;
                var receivedBytes = new byte[_lastBytesReceivedCount];

                _buffer.ToList().CopyTo(0, receivedBytes, 0, _lastBytesReceivedCount);
                messageData.AddRange(receivedBytes.ToList());

                receiveCount++;
                totalBytesReceieved += _lastBytesReceivedCount;

                EventOccurred?.Invoke(
                    new ServerEventInfo
                    {
                        EventType = ServerEventType.ReceivedDataFromSocket,
                        ReceiveBytesCount = receiveCount,
                        CurrentBytesReceivedFromSocket = _lastBytesReceivedCount,
                        TotalBytesReceivedFromSocket = totalBytesReceieved,
                        MessageLength = messageLength
                    });
            }

            var unreadByteCount = messageData.Count - messageLength;
            if (unreadByteCount <= 0)
            {
                return Result.Ok(messageData.ToArray());
            }

            var unreadBytes = new byte[unreadByteCount];
            messageData.CopyTo(messageData.Count, unreadBytes, 0, unreadByteCount);

            _unreadBytes = unreadBytes.ToList();
            messageData = messageData.GetRange(0, messageLength);

            return Result.Ok(messageData.ToArray());
        }

        private async Task<Result> ProcessRequestAsync(byte[] messageData, CancellationToken token)
        {
            EventOccurred?.Invoke(new ServerEventInfo { EventType = ServerEventType.DetermineRequestTypeStarted });

            var transferTypeData = MessageUnwrapper.ReadInt32(messageData).ToString();
            var transferType = (RequestType)Enum.Parse(typeof(RequestType), transferTypeData);

            switch (transferType)
            {
                case RequestType.TextMessage:

                    EventOccurred?.Invoke(
                        new ServerEventInfo
                        {
                            EventType = ServerEventType.DetermineRequestTypeCompleted,
                            RequestType = RequestType.TextMessage
                        });

                    return ReceiveTextMessage(messageData, token);

                case RequestType.InboundFileTransfer:

                    EventOccurred?.Invoke(
                        new ServerEventInfo
                        {
                            EventType = ServerEventType.DetermineRequestTypeCompleted,
                            RequestType = RequestType.InboundFileTransfer
                        });

                    return await InboundFileTransferAsync(messageData, token).ConfigureAwait(false);

                case RequestType.OutboundFileTransfer:

                    EventOccurred?.Invoke(
                        new ServerEventInfo
                        {
                            EventType = ServerEventType.DetermineRequestTypeCompleted,
                            RequestType = RequestType.OutboundFileTransfer
                        });

                    return await OutboundFileTransferAsync(messageData, token).ConfigureAwait(false);

                case RequestType.DataIsNoLongerBeingReceived:

                    EventOccurred?.Invoke(
                        new ServerEventInfo
                        {
                            EventType = ServerEventType.DetermineMessageLengthCompleted,
                            RequestType = RequestType.DataIsNoLongerBeingReceived
                        });

                    return AbortOutboundFileTransfer(messageData, token);

                case RequestType.GetFileList:

                    EventOccurred?.Invoke(
                        new ServerEventInfo
                        {
                            EventType = ServerEventType.DetermineRequestTypeCompleted,
                            RequestType = RequestType.GetFileList
                        });

                    return await SendFileList(messageData, token).ConfigureAwait(false);

                case RequestType.ReceiveFileList:

                    EventOccurred?.Invoke(
                        new ServerEventInfo
                        {
                            EventType = ServerEventType.DetermineRequestTypeCompleted,
                            RequestType = RequestType.ReceiveFileList
                        });

                    return ReceiveFileList(messageData, token);

                case RequestType.TransferFolderPathRequest:

                    EventOccurred?.Invoke(
                        new ServerEventInfo
                        {
                            EventType = ServerEventType.DetermineRequestTypeCompleted,
                            RequestType = RequestType.TransferFolderPathRequest
                        });

                    return await SendTransferFolderResponseAsync(messageData, token).ConfigureAwait(false);

                case RequestType.TransferFolderPathResponse:

                    EventOccurred?.Invoke(
                        new ServerEventInfo
                        {
                            EventType = ServerEventType.DetermineRequestTypeCompleted,
                            RequestType = RequestType.TransferFolderPathResponse
                        });

                    return ReceiveTransferFolderResponse(messageData, token);

                case RequestType.PublicIpAddressRequest:

                    EventOccurred?.Invoke(
                        new ServerEventInfo
                        {
                            EventType = ServerEventType.DetermineRequestTypeCompleted,
                            RequestType = RequestType.PublicIpAddressRequest
                        });

                    return await SendPublicIpAddress(messageData, token).ConfigureAwait(false);

                case RequestType.PublicIpAddressResponse:

                    EventOccurred?.Invoke(
                        new ServerEventInfo
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

        private Result ReceiveTextMessage(byte[] buffer, CancellationToken token)
        {
            EventOccurred?.Invoke(new ServerEventInfo { EventType = ServerEventType.ReceiveTextMessageStarted });

            var(message,
                remoteIpAddress,
                remotePortNumber) = MessageUnwrapper.ReadTextMessage(buffer);

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
                fileSizeBytes,
                remoteIpAddress,
                remotePort) = MessageUnwrapper.ReadInboundFileTransferRequest(buffer);

            if (token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
            }

            EventOccurred?.Invoke(
                new ServerEventInfo
                {
                    EventType = ServerEventType.ReceiveInboundFileTransferInfoCompleted,
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

                return sendResult.Success ? Result.Ok() : sendResult;
            }

            var startTime = DateTime.Now;

            EventOccurred?.Invoke(new ServerEventInfo
            {
                EventType = ServerEventType.ReceiveFileBytesStarted,
                FileTransferStartTime = startTime,
                FileSizeInBytes = fileSizeBytes
            });

            var receiveFileResult =
                await ReceiveFileAsync(localFilePath, fileSizeBytes, _buffer, 0, _bufferSize, 0, _receiveTimeoutMs, token)
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

            EventOccurred?.Invoke(
                new ServerEventInfo
                {
                    EventType = ServerEventType.SendConfirmationMessageStarted,
                    ConfirmationMessage = ConfirmationMessage
                });

            var confirmationMessageData = Encoding.ASCII.GetBytes(ConfirmationMessage);

            var sendConfirmatinMessageResult =
                await _clientSocket.SendWithTimeoutAsync(confirmationMessageData, 0, confirmationMessageData.Length, 0, _sendTimeoutMs)
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

            if (_unreadBytes.Count > 0)
            {
                totalBytesReceived += _unreadBytes.Count;

                var writeBytesResult = FileHelper.WriteBytesToFile(localFilePath, _unreadBytes.ToArray(), _unreadBytes.Count);
                if (writeBytesResult.Failure)
                {
                    return writeBytesResult;
                }
            }

            //var receiveCount = 0;

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
                    var receiveBytesResult = await _clientSocket
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
                //receiveCount++;
                //var bytesRemaining = fileSizeInBytes - totalBytesReceived;

                //EventOccurred?.Invoke(new ServerEventInfo
                //{
                //    EventType = ServerEventType.ReceivedDataFromSocket,
                //    ReceiveBytesCount = receiveCount,
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
                            TotalBytesReceivedFromSocket = totalBytesReceived,
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

        private Result AbortOutboundFileTransfer(byte[] messageData, CancellationToken token)
        {
            (string requestorIpAddress,
                int requestorPortNumber) = MessageUnwrapper.ReadServerConnectionInfo(messageData);

            EventOccurred?.Invoke(
                new ServerEventInfo
                {
                    EventType = ServerEventType.AbortOutboundFileTransfer,
                    RemoteServerIpAddress = requestorIpAddress,
                    RemoteServerPortNumber = requestorPortNumber
                });

            if (token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
            }

            return Result.Ok();
        }

        private async Task<Result> SendFileList(byte[] buffer, CancellationToken token)
        {
            EventOccurred?.Invoke(new ServerEventInfo{ EventType = ServerEventType.ReceiveFileListRequestStarted});

            (string requestorIpAddress,
                int requestorPortNumber,
                string targetFolderPath) = MessageUnwrapper.ReadFileListRequest(buffer);

            EventOccurred?.Invoke(
                new ServerEventInfo
                {
                    EventType = ServerEventType.ReceiveFileListRequestCompleted,
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
                EventOccurred?.Invoke(new ServerEventInfo
                {
                    EventType = ServerEventType.SendFileListResponseStarted,
                    RemoteServerIpAddress = _localIpAddress,
                    RemoteServerPortNumber = _localPort,
                    FileInfoList = new List<(string, long)>(),
                    LocalFolder = targetFolderPath,
                });

                var message = $"{EmptyTransferFolderErrorMessage}: {TransferFolderPath}";

                var sendResult = await SendTextMessageAsync(
                        message,
                        requestorIpAddress,
                        requestorPortNumber,
                        _localIpAddress,
                        _localPort,
                        token)
                        .ConfigureAwait(false);

                EventOccurred?.Invoke(new ServerEventInfo { EventType = ServerEventType.SendFileListResponseCompleted });

                return sendResult.Success ? Result.Ok() : sendResult;
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

            EventOccurred?.Invoke(new ServerEventInfo { EventType = ServerEventType.ConnectToRemoteServerStarted });

            var transferSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            var connectResult =
                await transferSocket.ConnectWithTimeoutAsync(requestorIpAddress, requestorPortNumber, _connectTimeoutMs)
                    .ConfigureAwait(false);

            if (connectResult.Failure)
            {
                return connectResult;
            }

            EventOccurred?.Invoke(new ServerEventInfo { EventType = ServerEventType.ConnectToRemoteServerCompleted });

            EventOccurred?.Invoke(new ServerEventInfo
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
                await transferSocket.SendWithTimeoutAsync(messageLength, 0, messageLength.Length, 0, _sendTimeoutMs)
                    .ConfigureAwait(false);

            if (sendMessageResult.Failure)
            {
                return sendMessageResult;
            }

            sendMessageResult =
                await transferSocket.SendWithTimeoutAsync(messageData, 0, messageData.Length, 0, _sendTimeoutMs)
                    .ConfigureAwait(false);

            if (sendMessageResult.Failure)
            {
                return sendMessageResult;
            }

            EventOccurred?.Invoke(new ServerEventInfo { EventType = ServerEventType.SendFileListResponseCompleted });

            transferSocket.Shutdown(SocketShutdown.Both);
            transferSocket.Close();

            return Result.Ok();
        }

        private Result ReceiveFileList(byte[] buffer, CancellationToken token)
        {
            EventOccurred?.Invoke(new ServerEventInfo { EventType = ServerEventType.ReceiveFileListResponseStarted });

            var(remoteServerIp,
                remoteServerPort,
                localIp,
                localPort,
                transferFolder,
                fileInfoList) = MessageUnwrapper.ReadFileListResponse(buffer);

            if (token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
            }

            EventOccurred?.Invoke(
                new ServerEventInfo
                {
                    EventType = ServerEventType.ReceiveFileListResponseCompleted,
                    RemoteServerIpAddress = remoteServerIp,
                    RemoteServerPortNumber = remoteServerPort,
                    LocalIpAddress = localIp,
                    LocalPortNumber = localPort,
                    LocalFolder = transferFolder,
                    FileInfoList = fileInfoList
                });

            return Result.Ok();
        }

        private async Task<Result> SendTransferFolderResponseAsync(byte[] buffer, CancellationToken token)
        {
            EventOccurred?.Invoke(new ServerEventInfo { EventType = ServerEventType.ReceiveTransferFolderRequestStarted });

            (string requestorIpAddress,
                int requestorPortNumber) = MessageUnwrapper.ReadServerConnectionInfo(buffer);

            EventOccurred?.Invoke(
                new ServerEventInfo
                {
                    EventType = ServerEventType.ReceiveTransferFolderRequestCompleted,
                    RemoteServerIpAddress = requestorIpAddress,
                    RemoteServerPortNumber = requestorPortNumber
                });

            if (token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
            }

            EventOccurred?.Invoke(new ServerEventInfo { EventType = ServerEventType.ConnectToRemoteServerStarted });

            var transferSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            var connectResult =
                await transferSocket.ConnectWithTimeoutAsync(requestorIpAddress, requestorPortNumber, _connectTimeoutMs)
                    .ConfigureAwait(false);

            if (connectResult.Failure)
            {
                return connectResult;
            }

            EventOccurred?.Invoke(new ServerEventInfo { EventType = ServerEventType.ConnectToRemoteServerCompleted });

            EventOccurred?.Invoke(new ServerEventInfo
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
                await transferSocket.SendWithTimeoutAsync(messageLength, 0, messageLength.Length, 0, _sendTimeoutMs)
                    .ConfigureAwait(false);

            if (sendMessageResult.Failure)
            {
                return sendMessageResult;
            }

            sendMessageResult =
                await transferSocket.SendWithTimeoutAsync(messageData, 0, messageData.Length, 0, _sendTimeoutMs)
                    .ConfigureAwait(false);

            if (sendMessageResult.Failure)
            {
                return sendMessageResult;
            }

            EventOccurred?.Invoke(new ServerEventInfo { EventType = ServerEventType.SendTransferFolderRequestCompleted });

            transferSocket.Shutdown(SocketShutdown.Both);
            transferSocket.Close();

            return Result.Ok();
        }

        private Result ReceiveTransferFolderResponse(byte[] buffer, CancellationToken token)
        {
            EventOccurred?.Invoke(new ServerEventInfo { EventType = ServerEventType.ReceiveTransferFolderResponseStarted });

            var (remoteServerIp,
                remoteServerPort,
                transferFolder) = MessageUnwrapper.ReadTransferFolderResponse(buffer);

            if (token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
            }

            EventOccurred?.Invoke(
                new ServerEventInfo
                {
                    EventType = ServerEventType.ReceiveTransferFolderResponseCompleted,
                    RemoteServerIpAddress = remoteServerIp,
                    RemoteServerPortNumber = remoteServerPort,
                    RemoteFolder = transferFolder
                });

            return Result.Ok();
        }

        private async Task<Result> SendPublicIpAddress(byte[] buffer, CancellationToken token)
        {
            EventOccurred?.Invoke(new ServerEventInfo { EventType = ServerEventType.ReceiveTransferFolderRequestStarted });

            (string requestorIpAddress,
                int requestorPortNumber) = MessageUnwrapper.ReadServerConnectionInfo(buffer);

            EventOccurred?.Invoke(
                new ServerEventInfo
                {
                    EventType = ServerEventType.ReceivePublicIpRequestCompleted,
                    RemoteServerIpAddress = requestorIpAddress,
                    RemoteServerPortNumber = requestorPortNumber
                });

            if (token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
            }

            EventOccurred?.Invoke(new ServerEventInfo { EventType = ServerEventType.ConnectToRemoteServerStarted });

            var transferSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            var connectResult =
                await transferSocket.ConnectWithTimeoutAsync(requestorIpAddress, requestorPortNumber, _connectTimeoutMs)
                    .ConfigureAwait(false);

            if (connectResult.Failure)
            {
                return connectResult;
            }

            EventOccurred?.Invoke(new ServerEventInfo { EventType = ServerEventType.ConnectToRemoteServerCompleted });

            var publicIp = IPAddress.None.ToString();
            var publicIpResult = await Network.GetPublicIPv4AddressAsync().ConfigureAwait(false);
            if (publicIpResult.Success)
            {
                publicIp = publicIpResult.Value.ToString();
            }

            EventOccurred?.Invoke(new ServerEventInfo
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
                await transferSocket.SendWithTimeoutAsync(messageLength, 0, messageLength.Length, 0, _sendTimeoutMs)
                    .ConfigureAwait(false);

            if (sendMessageResult.Failure)
            {
                return sendMessageResult;
            }

            sendMessageResult =
                await transferSocket.SendWithTimeoutAsync(messageData, 0, messageData.Length, 0, _sendTimeoutMs)
                    .ConfigureAwait(false);

            if (sendMessageResult.Failure)
            {
                return sendMessageResult;
            }

            EventOccurred?.Invoke(new ServerEventInfo { EventType = ServerEventType.SendPublicIpResponseCompleted });

            transferSocket.Shutdown(SocketShutdown.Both);
            transferSocket.Close();

            return Result.Ok();
        }

        private Result ReceivePublicIpAddress(byte[] buffer, CancellationToken token)
        {
            EventOccurred?.Invoke(new ServerEventInfo { EventType = ServerEventType.ReceivePublicIpResponseStarted });

            var (remoteServerIp,
                remoteServerPort,
                publicIpAddress) = MessageUnwrapper.ReadPublicIpAddressResponse(buffer);

            if (token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
            }

            EventOccurred?.Invoke(
                new ServerEventInfo
                {
                    EventType = ServerEventType.ReceivePublicIpResponseCompleted,
                    RemoteServerIpAddress = remoteServerIp,
                    RemoteServerPortNumber = remoteServerPort,
                    PublicIpAddress = publicIpAddress
                });

            return Result.Ok();
        }

        private async Task<Result> SendGenericMessageToClient(
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
                    EventType = eventStarted,
                    RemoteServerIpAddress = remoteServerIpAddress,
                    RemoteServerPortNumber = remoteServerPort
                });

            var messageData =
                MessageWrapper.ConstructGenericMessage(requestType, localIpAddress, localPort);

            var messageLength = BitConverter.GetBytes(messageData.Length);

            var sendMessageResult =
                await transferSocket.SendWithTimeoutAsync(messageLength, 0, messageLength.Length, 0, _sendTimeoutMs)
                    .ConfigureAwait(false);

            if (sendMessageResult.Failure)
            {
                return sendMessageResult;
            }

            sendMessageResult =
                await transferSocket.SendWithTimeoutAsync(messageData, 0, messageData.Length, 0, _sendTimeoutMs)
                    .ConfigureAwait(false);

            if (sendMessageResult.Failure)
            {
                return sendMessageResult;
            }

            EventOccurred?.Invoke(new ServerEventInfo { EventType = eventCompleted });

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

            var messageData = MessageWrapper.ConstuctTextMessageRequest(message, localIpAddress, localPort);
            var messageLength = BitConverter.GetBytes(messageData.Length);

            var sendMessageResult =
                await transferSocket.SendWithTimeoutAsync(messageLength, 0, messageLength.Length, 0, _sendTimeoutMs)
                    .ConfigureAwait(false);

            if (sendMessageResult.Failure)
            {
                return sendMessageResult;
            }

            sendMessageResult =
                await transferSocket.SendWithTimeoutAsync(messageData, 0, messageData.Length, 0, _sendTimeoutMs)
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

            var messageData =
                MessageWrapper.ConstructOutboundFileTransferRequest(localFilePath, fileSizeBytes, _localIpAddress, _localPort, remoteFolderPath);

            var messageLength = BitConverter.GetBytes(messageData.Length);

            var sendMessageResult =
                await transferSocket.SendWithTimeoutAsync(messageLength, 0, messageLength.Length, 0, _sendTimeoutMs)
                    .ConfigureAwait(false);

            if (sendMessageResult.Failure)
            {
                return sendMessageResult;
            }

            sendMessageResult =
                await transferSocket.SendWithTimeoutAsync(messageData, 0, messageData.Length, 0, _sendTimeoutMs)
                    .ConfigureAwait(false);

            if (sendMessageResult.Failure)
            {
                return sendMessageResult;
            }

            EventOccurred?.Invoke(new ServerEventInfo { EventType = ServerEventType.SendOutboundFileTransferInfoCompleted });
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

        private async Task<Result> ReceiveConfirmationAsync(Socket transferSocket)
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
                await transferSocket.SendWithTimeoutAsync(messageLength, 0, messageLength.Length, 0, _sendTimeoutMs)
                    .ConfigureAwait(false);

            if (sendMessageResult.Failure)
            {
                return sendMessageResult;
            }

            sendMessageResult =
                await transferSocket.SendWithTimeoutAsync(messageData, 0, messageData.Length, 0, _sendTimeoutMs)
                    .ConfigureAwait(false);

            if (sendMessageResult.Failure)
            {
                return sendMessageResult;
            }

            EventOccurred?.Invoke(new ServerEventInfo { EventType = ServerEventType.SendInboundFileTransferInfoCompleted });

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
                    EventType = ServerEventType.SendFileListRequestStarted,
                    RemoteServerIpAddress = remoteServerIpAddress,
                    RemoteServerPortNumber = remoteServerPort,
                    RemoteFolder = targetFolder
                });

            var messageData =
                MessageWrapper.ConstructFileListRequest(localIpAddress, localPort, targetFolder);

            var messageLength = BitConverter.GetBytes(messageData.Length);

            var sendMessageResult =
                await transferSocket.SendWithTimeoutAsync(messageLength, 0, messageLength.Length, 0, _sendTimeoutMs)
                    .ConfigureAwait(false);

            if (sendMessageResult.Failure)
            {
                return sendMessageResult;
            }

            sendMessageResult =
                await transferSocket.SendWithTimeoutAsync(messageData, 0, messageData.Length, 0, _sendTimeoutMs)
                    .ConfigureAwait(false);

            if (sendMessageResult.Failure)
            {
                return sendMessageResult;
            }

            EventOccurred?.Invoke(new ServerEventInfo { EventType = ServerEventType.SendFileListRequestCompleted });

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