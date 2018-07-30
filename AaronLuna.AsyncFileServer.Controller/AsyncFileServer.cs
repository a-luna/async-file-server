using AaronLuna.Common.IO;

namespace AaronLuna.AsyncFileServer.Controller
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;

    using Common.Extensions;
    using Common.Logging;
    using Common.Network;
    using Common.Result;

    using Model;
    using Utilities;

    public class AsyncFileServer
    {
        const string NotInitializedMessage =
            "Server is unitialized and cannot handle incoming connections";
        
        int _initialized;
        int _busy;
        int _shutdownInitiated;
        int _listening;
        int _textSessionId;
        int _requestId;
        int _fileTransferId;

        readonly Logger _log = new Logger(typeof(AsyncFileServer));
        readonly List<ServerEvent> _eventLog;
        readonly Socket _listenSocket;
        ServerSettings _settings;

        CancellationToken _token;
        static readonly object RequestQueueLock = new object();
        static readonly object TransferQueueLock = new object();
        int TransferRetryLimit => _settings.TransferRetryLimit;
        TimeSpan RetryLimitLockout => _settings.RetryLimitLockout;
        int ListenBacklogSize => _settings.SocketSettings.ListenBacklogSize;

        bool ServerIsInitialized
        {
            get => Interlocked.CompareExchange(ref _initialized, 1, 1) == 1;
            set
            {
                if (value) Interlocked.CompareExchange(ref _initialized, 1, 0);
                else Interlocked.CompareExchange(ref _initialized, 0, 1);
            }
        }

        bool ServerIsListening
        {
            get => Interlocked.CompareExchange(ref _listening, 1, 1) == 1;
            set
            {
                if (value) Interlocked.CompareExchange(ref _listening, 1, 0);
                else Interlocked.CompareExchange(ref _listening, 0, 1);
            }
        }

        bool ServerIsBusy
        {
            get => Interlocked.CompareExchange(ref _busy, 1, 1) == 1;
            set
            {
                if (value) Interlocked.CompareExchange(ref _busy, 1, 0);
                else Interlocked.CompareExchange(ref _busy, 0, 1);
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

        public AsyncFileServer()
        {
            ServerIsInitialized = false;
            ServerIsListening = false;
            ServerIsBusy = false;
            ShutdownInitiated = false;

            MyInfo = new ServerInfo()
            {
                TransferFolder = GetDefaultTransferFolder(),
                Name = "AsyncFileServer"
            };

            ErrorLog = new List<ServerError>();
            Requests = new List<ServerRequestController>();
            FileTransfers = new List<FileTransferController>();
            TextSessions = new List<TextSession>();

            _textSessionId = 1;
            _requestId = 1;
            _fileTransferId = 1;
            _listenSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _eventLog = new List<ServerEvent>();            
            _settings = new ServerSettings();

            string GetDefaultTransferFolder()
            {
                var defaultPath = $"{Directory.GetCurrentDirectory()}{Path.DirectorySeparatorChar}transfer";

                if (!Directory.Exists(defaultPath))
                {
                    Directory.CreateDirectory(defaultPath);
                }

                return defaultPath;
            }
        }

        public AsyncFileServer(string name, ServerSettings settings) :this()
        {
            MyInfo = new ServerInfo
            {
                TransferFolder = settings.LocalServerFolderPath,
                Name = name
            };

            UpdateSettings(settings);
        }
        
        public bool IsListening => ServerIsListening;

        public ServerInfo MyInfo { get; set; }
        public List<TextSession> TextSessions { get; }
        public List<FileTransferController> FileTransfers { get; }
        public List<ServerRequestController> Requests { get; }
        public List<ServerError> ErrorLog { get; }

        public event EventHandler<ServerEvent> EventOccurred;
        public event EventHandler<ServerEvent> SocketEventOccurred;
        public event EventHandler<ServerEvent> FileTransferProgress;

        public override string ToString()
        {
            var name = string.IsNullOrEmpty(MyInfo.Name)
                ? "AsyncFileServer"
                : MyInfo.Name;

            var localEndPoint = $"{MyInfo.LocalIpAddress}:{MyInfo.PortNumber}";

            var inRequestCount =
                    Requests.Select(r => r)
                        .Where(r => r.Direction == ServerRequestDirection.Received)
                        .ToList().Count;

            var outRequestCount =
                Requests.Select(r => r)
                    .Where(r => r.Direction == ServerRequestDirection.Sent)
                    .ToList().Count;

            var inTransferCount =
                FileTransfers.Select(ft => ft)
                    .Where(ft => ft.TransferDirection == FileTransferDirection.Inbound)
                    .ToList().Count;

            var outTransferCount =
                FileTransfers.Select(ft => ft)
                    .Where(ft => ft.TransferDirection == FileTransferDirection.Outbound)
                    .ToList().Count;

            var textMessageCount = 0;
            foreach (var textSession in TextSessions)
            {
                textMessageCount += textSession.MessageCount;
            }

            return
                $"{name} [{localEndPoint}] " +
                $"[Requests In: {inRequestCount} Out: {outRequestCount}] " +
                $"[Transfers In: {inTransferCount} Out: {outTransferCount}] " +
                $"[Total Messages: {textMessageCount}/{TextSessions.Count} Sessions]";
        }

        public void UpdateSettings(ServerSettings settings)
        {
            MyInfo.TransferFolder = settings.LocalServerFolderPath;
            _settings = settings;
        }

        public Result<TextSession> GetTextSessionById(int id)
        {
            var matches = TextSessions.Select(ts => ts).Where(ts => ts.Id == id).ToList();

            if (matches.Count == 0)
            {
                return Result.Fail<TextSession>($"No text session was found with an ID value of {id}");
            }

            if (matches.Count > 1)
            {
                return Result.Fail<TextSession>($"Found {matches.Count} text sessions with the same ID value of {id}");
            }

            return Result.Ok(matches[0]);
        }

        public Result<ServerRequestController> GetRequestById(int id)
        {
            var matches =
                Requests.Select(r => r)
                    .Where(r => r.Id == id)
                    .ToList();

            if (matches.Count == 0)
            {
                return Result.Fail<ServerRequestController>(
                    $"No request was found with an ID value of {id}");
            }

            return matches.Count == 1
                ? Result.Ok(matches[0])
                : Result.Fail<ServerRequestController>(
                    $"Found {matches.Count} requests with the same ID value of {id}");
        }

        public Result<FileTransferController> GetFileTransferById(int id)
        {
            var matches = FileTransfers.Select(t => t).Where(t => t.Id == id).ToList();
            if (matches.Count == 0)
            {
                return Result.Fail<FileTransferController>(
                    $"No file transfer was found with an ID value of {id}");
            }

            return matches.Count == 1
                ? Result.Ok(matches[0])
                : Result.Fail<FileTransferController>(
                    $"Found {matches.Count} file transfers with the same ID value of {id}");
        }

        public List<ServerEvent> GetEventLogForFileTransfer(int fileTransferId, LogLevel logLevel)
        {
            var eventLog = new List<ServerEvent>();

            var eventsMatchingFileTransferId =
                _eventLog.Select(e => e)
                    .Where(e => e.FileTransferId == fileTransferId)
                    .ToList();

            eventLog.AddRange(eventsMatchingFileTransferId);

            return eventLog.ApplyFilter(logLevel);
        }

        public List<ServerEvent> GetEventLogForRequest(int requestId)
        {
            var eventLog = new List<ServerEvent>();

            var eventsMatchingRequestId =
                _eventLog.Select(e => e)
                    .Where(e => e.RequestId == requestId)
                    .ToList();

            eventLog.AddRange(eventsMatchingRequestId);

            return eventLog.ApplyFilter(LogLevel.Debug);
        }

        public List<ServerEvent> GetCompleteEventLog(LogLevel logLevel)
        {
            var eventLog = new List<ServerEvent>();
            eventLog.AddRange(_eventLog);

            return eventLog.ApplyFilter(logLevel);

        }

        public async Task InitializeAsync(ServerSettings settings)
        {
            if (ServerIsInitialized) return;

            UpdateSettings(settings);
            var cidrIp = _settings.LocalNetworkCidrIp;
            var port = _settings.LocalServerPortNumber;

            var getLocalIp = NetworkUtilities.GetLocalIPv4Address(cidrIp);

            var localIp = getLocalIp.Success
                ? getLocalIp.Value
                : IPAddress.Loopback;

            var getPublicIp =
                await NetworkUtilities.GetPublicIPv4AddressAsync().ConfigureAwait(false);

            var publicIp = getPublicIp.Success
                ? getPublicIp.Value
                : IPAddress.None;

            MyInfo.PortNumber = port;
            MyInfo.LocalIpAddress = localIp;
            MyInfo.PublicIpAddress = publicIp;
            MyInfo.Platform = Environment.OSVersion.Platform.ToServerPlatform();

            if (getLocalIp.Success)
            {
                MyInfo.SessionIpAddress = localIp;
            }
            else if (getPublicIp.Success)
            {
                MyInfo.SessionIpAddress = publicIp;
            }
            
            ServerIsInitialized = true;
        }

        public async Task<Result> RunAsync(CancellationToken token)
        {
            if (!ServerIsInitialized)
            {
                return Result.Fail(NotInitializedMessage);
            }

            _token = token;
            Logger.Start("server.log");

            var startListening = Listen(MyInfo.PortNumber);
            if (startListening.Failure)
            {
                return startListening;
            }

            ServerIsListening = true;
            var runServer = await HandleIncomingRequestsAsync().ConfigureAwait(false);

            ServerIsListening = false;
            ShutdownListenSocket();

            return runServer;
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
                return Result.Fail($"{ex.Message} ({ex.GetType()} raised in method AsyncFileServer.Listen)");
            }

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ServerStartedListening,
                LocalPortNumber = MyInfo.PortNumber
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            return Result.Ok();
        }

        async Task<Result> HandleIncomingRequestsAsync()
        {
            // Main loop. Server handles incoming connections until shutdown command is received
            // or an error is encountered
            while (true)
            {
                if (FileTransferPending())
                {
                    _eventLog.Add(new ServerEvent
                    {
                        EventType = ServerEventType.PendingFileTransfer,
                        ItemsInQueueCount = PendingTransferCount()
                    });

                    EventOccurred?.Invoke(this, _eventLog.Last());
                }

                var acceptConnection = await AcceptConnectionFromRemoteServerAsync().ConfigureAwait(false);
                if (acceptConnection.Failure) continue;

                var inboundRequest = acceptConnection.Value;
                var receiveRequest = await inboundRequest.ReceiveServerRequestAsync().ConfigureAwait(false);

                if (receiveRequest.Failure)
                {
                    ErrorLog.Add(new ServerError(receiveRequest.Error));

                    _eventLog.Add(new ServerEvent
                    {
                        EventType = ServerEventType.ErrorOccurred,
                        ErrorMessage = receiveRequest.Error
                    });

                    EventOccurred?.Invoke(this, _eventLog.Last());
                }

                lock (RequestQueueLock)
                {
                    Requests.Add(inboundRequest);
                    _requestId++;
                }

                if (_token.IsCancellationRequested || ShutdownInitiated) return Result.Ok();
                if (ServerIsBusy) continue;

                await ProcessRequestAsync(inboundRequest).ConfigureAwait(false);
                await ProcessRequestBacklogAsync().ConfigureAwait(false);
            }

            bool FileTransferPending()
            {
                return PendingTransferCount() > 0;
            }

            int PendingTransferCount()
            {
                var pendingTransfers =
                    FileTransfers.Select(ft => ft)
                        .Where(ft => ft.TransferDirection == FileTransferDirection.Inbound
                                     && ft.Status == FileTransferStatus.Pending)
                        .ToList();

                return pendingTransfers.Count;
            }
        }

        async Task<Result<ServerRequestController>> AcceptConnectionFromRemoteServerAsync()
        {
            var acceptConnection = await _listenSocket.AcceptTaskAsync(_token).ConfigureAwait(false);
            if (acceptConnection.Failure)
            {
                ErrorLog.Add(new ServerError(acceptConnection.Error));

                _eventLog.Add(new ServerEvent
                {
                    EventType = ServerEventType.ErrorOccurred,
                    ErrorMessage = acceptConnection.Error
                });

                EventOccurred?.Invoke(this, _eventLog.Last());

                return Result.Fail<ServerRequestController>(acceptConnection.Error);
            }

            var socket = acceptConnection.Value;
            var remoteServerIpString = socket.RemoteEndPoint.ToString().Split(':')[0];
            var remoteServerIpAddress = NetworkUtilities.ParseSingleIPv4Address(remoteServerIpString).Value;

            var inboundRequest =
                new ServerRequestController(_requestId, _settings, socket);

            inboundRequest.EventOccurred += HandleEventOccurred;
            inboundRequest.SocketEventOccurred += HandleSocketEventOccurred;

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ConnectionAccepted,
                RemoteServerIpAddress = remoteServerIpAddress,
                RequestId = inboundRequest.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            return Result.Ok(inboundRequest);
        }

        async Task<Result> ProcessRequestAsync(ServerRequestController inboundRequest)
        {
            ServerIsBusy = true;
            inboundRequest.Status = ServerRequestStatus.InProgress;

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ProcessRequestStarted,
                RequestType = inboundRequest.RequestType,
                RequestId = inboundRequest.Id,
                RemoteServerIpAddress = inboundRequest.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = inboundRequest.RemoteServerInfo.PortNumber
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            var result = await ProcessRequestTypeAsync(inboundRequest);
            inboundRequest.ShutdownSocket();

            if (result.Failure)
            {
                inboundRequest.Status = ServerRequestStatus.Error;
                ErrorLog.Add(new ServerError(result.Error));

                _eventLog.Add(new ServerEvent
                {
                    EventType = ServerEventType.ErrorOccurred,
                    ErrorMessage = result.Error,
                    RequestId = inboundRequest.Id
                });

                EventOccurred?.Invoke(this, _eventLog.Last());

                return result;
            }

            inboundRequest.Status = ServerRequestStatus.Processed;

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ProcessRequestComplete,
                RequestType = inboundRequest.RequestType,
                RequestId = inboundRequest.Id,
                RemoteServerIpAddress = inboundRequest.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = inboundRequest.RemoteServerInfo.PortNumber
            });

            EventOccurred?.Invoke(this, _eventLog.Last());
            ServerIsBusy = false;

            return Result.Ok();

            async Task<Result> ProcessRequestTypeAsync(ServerRequestController request)
            {
                result = Result.Ok();

                switch (request.RequestType)
                {
                    case ServerRequestType.TextMessage:
                        result = ReceiveTextMessage(request);
                        break;

                    case ServerRequestType.InboundFileTransferRequest:
                        result = await HandleInboundFileTransferRequestAsync(request).ConfigureAwait(false);
                        break;

                    case ServerRequestType.OutboundFileTransferRequest:
                        result = await HandleOutboundFileTransferRequestAsync(request).ConfigureAwait(false);
                        break;

                    case ServerRequestType.RequestedFileDoesNotExist:
                        result = HandleRequestedFileDoesNotExist(request);
                        break;

                    case ServerRequestType.FileTransferRejected:
                        result = HandleFileTransferRejected(request);
                        break;

                    case ServerRequestType.FileTransferAccepted:
                        result = await HandleFileTransferAcceptedAsync(request, _token).ConfigureAwait(false);
                        break;

                    case ServerRequestType.FileTransferStalled:
                        result = HandleFileTransferStalled(request);
                        break;

                    case ServerRequestType.FileTransferComplete:
                        result = HandleFileTransferComplete(request);
                        break;

                    case ServerRequestType.RetryOutboundFileTransfer:
                        result = await HandleRetryFileTransferAsync(request).ConfigureAwait(false);
                        break;

                    case ServerRequestType.RetryLimitExceeded:
                        result = HandleRetryLimitExceeded(request);
                        break;

                    case ServerRequestType.FileListRequest:
                        result = await HandleFileListRequestAsync(request).ConfigureAwait(false);
                        break;

                    case ServerRequestType.FileListResponse:
                        HandleFileListResponse(request);
                        break;

                    case ServerRequestType.NoFilesAvailableForDownload:
                        HandleNoFilesAvailableForDownload(request);
                        break;

                    case ServerRequestType.RequestedFolderDoesNotExist:
                        HandleRequestedFolderDoesNotExist(request);
                        break;

                    case ServerRequestType.ServerInfoRequest:
                        result = await HandleServerInfoRequest(request).ConfigureAwait(false);
                        break;

                    case ServerRequestType.ServerInfoResponse:
                        HandleServerInfoResponse(request);
                        break;

                    case ServerRequestType.ShutdownServerCommand:
                        HandleShutdownServerCommand(request);
                        break;

                    default:
                        var error = $"Unable to determine request type, value of '{request.RequestType}' is invalid.";
                        return Result.Fail(error);
                }

                return result;
            }
        }

        void HandleEventOccurred(object sender, ServerEvent e)
        {
            _eventLog.Add(e);
            EventOccurred?.Invoke(sender, e);
        }

        void HandleSocketEventOccurred(object sender, ServerEvent e)
        {
            _eventLog.Add(e);
            SocketEventOccurred?.Invoke(sender, e);
        }

        void HandleFileTransferProgress(object sender, ServerEvent e)
        {
            _eventLog.Add(e);
            FileTransferProgress?.Invoke(sender, e);
        }

        async Task<Result> ProcessRequestBacklogAsync()
        {
            if (QueueIsEmpty()) return Result.Ok();

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ProcessRequestBacklogStarted,
                ItemsInQueueCount = PendingRequestCount()
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            var backlog =
                    Requests.Select(r => r)
                        .Where(r => r.Status == ServerRequestStatus.Pending)
                        .ToList();

            foreach (var request in backlog)
            {
                var processRequest = await ProcessRequestAsync(request).ConfigureAwait(false);
                if (processRequest.Success) continue;

                request.Status = ServerRequestStatus.Error;
                ErrorLog.Add(new ServerError(processRequest.Error));

                _eventLog.Add(new ServerEvent
                {
                    EventType = ServerEventType.ErrorOccurred,
                    ErrorMessage = processRequest.Error,
                    RequestId = request.Id
                });

                EventOccurred?.Invoke(this, _eventLog.Last());

                return processRequest;
            }

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ProcessRequestBacklogComplete,
                ItemsInQueueCount = PendingRequestCount()
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            return Result.Ok();

            bool QueueIsEmpty()
            {
                List<ServerRequestController> pendingRequests;

                lock (RequestQueueLock)
                {
                    pendingRequests =
                        Requests.Select(r => r)
                            .Where(r => r.Status == ServerRequestStatus.Pending)
                            .ToList();
                }

                return pendingRequests.Count == 0;
            }

            int PendingRequestCount()
            {
                List<ServerRequestController> pendingRequests;

                lock (RequestQueueLock)
                {
                    pendingRequests =
                        Requests.Select(r => r)
                            .Where(r => r.Status == ServerRequestStatus.Pending)
                            .ToList();
                }

                return pendingRequests.Count;
            }
        }

        public async Task<Result> SendTextMessageAsync(
            string message,
            IPAddress remoteServerIpAddress,
            int remoteServerPort)
        {
            if (string.IsNullOrEmpty(message))
            {
                return Result.Fail("Message is null or empty string.");
            }

            var remoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);
            var textSessionId = GetTextSessionIdForRemoteServer(remoteServerInfo);

            var outboundRequest = new ServerRequestController(_requestId, _settings, remoteServerInfo);
            outboundRequest.EventOccurred += HandleEventOccurred;
            outboundRequest.SocketEventOccurred += HandleSocketEventOccurred;

            lock (RequestQueueLock)
            {
                Requests.Add(outboundRequest);
                _requestId++;
            }
            
            var requestBytes =
                ServerRequestDataBuilder.ConstructRequestWithStringValue(
                    ServerRequestType.TextMessage,
                    MyInfo.LocalIpAddress.ToString(),
                    MyInfo.PortNumber,
                    message);

            var sendRequestStartEvent = new ServerEvent
            {
                EventType = ServerEventType.SendTextMessageStarted,
                TextMessage = message,
                TextSessionId = textSessionId,
                RemoteServerIpAddress = remoteServerIpAddress,
                RemoteServerPortNumber = remoteServerPort,
                RequestId = _requestId
            };

            var sendRequestCompleteEvent = new ServerEvent
            {
                EventType = ServerEventType.SendTextMessageComplete,
                RequestId = _requestId
            };
            
            var sendRequest = await
                outboundRequest.SendServerRequestAsync(
                    requestBytes,
                    sendRequestStartEvent,
                    sendRequestCompleteEvent).ConfigureAwait(false);

            if (sendRequest.Failure)
            {
                outboundRequest.Status = ServerRequestStatus.Error;
                ErrorLog.Add(new ServerError(sendRequest.Error));

                _eventLog.Add(new ServerEvent
                {
                    EventType = ServerEventType.ErrorOccurred,
                    ErrorMessage = sendRequest.Error,
                    RequestId = outboundRequest.Id
                });

                EventOccurred?.Invoke(this, _eventLog.Last());

                return sendRequest;
            }

            var newMessage = new TextMessage
            {
                SessionId = textSessionId,
                TimeStamp = DateTime.Now,
                Author = TextMessageAuthor.Self,
                Message = message,
                Unread = false
            };

            var textSession = GetTextSessionById(textSessionId).Value;
            textSession.Messages.Add(newMessage);

            return Result.Ok();
        }

        Result ReceiveTextMessage(ServerRequestController inboundRequest)
        {
            var getTextMessage = inboundRequest.GetTextMessage();
            if (getTextMessage.Failure)
            {
                return getTextMessage;
            }

            var textSessionId = GetTextSessionIdForRemoteServer(inboundRequest.RemoteServerInfo);

            var newMessage = getTextMessage.Value;
            newMessage.SessionId = textSessionId;

            var textSession = GetTextSessionById(textSessionId).Value;
            textSession.Messages.Add(newMessage);

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceivedTextMessage,
                TextMessage = newMessage.Message,
                RemoteServerIpAddress = inboundRequest.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = inboundRequest.RemoteServerInfo.PortNumber,
                TextSessionId = textSessionId,
                RequestId = inboundRequest.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            return Result.Ok();
        }

        int GetTextSessionIdForRemoteServer(ServerInfo remoteServerInfo)
        {
            TextSession match = null;
            foreach (var textSession in TextSessions)
            {
                if (!textSession.RemoteServerInfo.IsEqualTo(remoteServerInfo)) continue;

                match = textSession;
                break;
            }

            if (match != null)
            {
                return match.Id;
            }

            var newTextSession = new TextSession
            {
                Id = _textSessionId,
                RemoteServerInfo = remoteServerInfo
            };

            TextSessions.Add(newTextSession);
            _textSessionId++;

            return newTextSession.Id;
        }

        public async Task<Result> SendFileAsync
        (IPAddress remoteServerIpAddress,
            int remoteServerPort,
            string remoteServerName,
            string fileName,
            long fileSizeInBytes,
            string localFolderPath,
            string remoteFolderPath)
        {
            var localFilePath = Path.Combine(localFolderPath, fileName);
            if (!File.Exists(localFilePath))
            {
                return Result.Fail("File does not exist: " + localFilePath);
            }

            var remoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort)
            {
                TransferFolder = remoteFolderPath,
                Name = remoteServerName
            };

            var outboundFileTransfer = new FileTransferController(_fileTransferId, _settings);
            outboundFileTransfer.EventOccurred += HandleEventOccurred;
            outboundFileTransfer.SocketEventOccurred += HandleSocketEventOccurred;
            outboundFileTransfer.FileTransferProgress += HandleFileTransferProgress;

            outboundFileTransfer.Initialize(
                FileTransferDirection.Outbound,
                FileTransferInitiator.Self,
                MyInfo,
                remoteServerInfo,
                fileName,
                fileSizeInBytes,
                localFolderPath,
                remoteFolderPath);

            lock (TransferQueueLock)
            {
                FileTransfers.Add(outboundFileTransfer);
                _fileTransferId++;
            }

            var sendRequest = await
                SendOutboundFileTransferRequestAsync(outboundFileTransfer).ConfigureAwait(false);

            if (sendRequest.Success) return Result.Ok();

            ErrorLog.Add(new ServerError(sendRequest.Error));

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ErrorOccurred,
                ErrorMessage = sendRequest.Error,
                FileTransferId = outboundFileTransfer.Id,
                RequestId = outboundFileTransfer.RequestId
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            return sendRequest;
        }

        async Task<Result> HandleOutboundFileTransferRequestAsync(ServerRequestController inboundRequest)
        {
            var getFileTransfer =
                inboundRequest.GetOutboundFileTransfer(_fileTransferId, _settings, MyInfo);

            if (getFileTransfer.Failure)
            {
                return getFileTransfer;
            }

            // TODO: Create logic to check stalled file transfers that are under lockout and if this request matches remoteserver info + localfilepath, send a new filetranserresponse = rejected_retrylimitexceeded. maybe we should penalize them for trying to subvert our lockout policy?
            var outboundFileTransfer = getFileTransfer.Value;
            outboundFileTransfer.EventOccurred += HandleEventOccurred;
            outboundFileTransfer.SocketEventOccurred += HandleSocketEventOccurred;
            outboundFileTransfer.FileTransferProgress += HandleFileTransferProgress;

            lock (TransferQueueLock)
            {
                FileTransfers.Add(outboundFileTransfer);
                _fileTransferId++;
            }

            if (!File.Exists(outboundFileTransfer.LocalFilePath))
            {
                outboundFileTransfer.TransferResponseCode = outboundFileTransfer.RemoteServerTransferId;

                return await SendFileTransferResponseAsync(
                    ServerRequestType.RequestedFileDoesNotExist,
                    outboundFileTransfer,
                    ServerEventType.SendNotificationFileDoesNotExistStarted,
                    ServerEventType.SendNotificationFileDoesNotExistComplete).ConfigureAwait(false);
            }

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceivedOutboundFileTransferRequest,
                LocalFolder = outboundFileTransfer.LocalFolderPath,
                FileName = outboundFileTransfer.FileName,
                FileSizeInBytes = outboundFileTransfer.FileSizeInBytes,
                RemoteFolder = outboundFileTransfer.RemoteFolderPath,
                RemoteServerIpAddress = outboundFileTransfer.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = outboundFileTransfer.RemoteServerInfo.PortNumber,
                FileTransferId = outboundFileTransfer.Id,
                RequestId = inboundRequest.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            return await SendOutboundFileTransferRequestAsync(outboundFileTransfer).ConfigureAwait(false);
        }

        async Task<Result> SendOutboundFileTransferRequestAsync(FileTransferController outboundFileTransfer)
        {
            var outboundRequest =
                new ServerRequestController(_requestId, _settings, outboundFileTransfer.RemoteServerInfo);

            outboundRequest.EventOccurred += HandleEventOccurred;
            outboundRequest.SocketEventOccurred += HandleSocketEventOccurred;
            outboundRequest.FileTransferId = outboundFileTransfer.Id;

            lock (RequestQueueLock)
            {
                Requests.Add(outboundRequest);
                _requestId++;
            }

            outboundFileTransfer.RequestId = outboundRequest.Id;

            var requestBytes =
                ServerRequestDataBuilder.ConstructOutboundFileTransferRequest(
                    MyInfo.LocalIpAddress.ToString(),
                    MyInfo.PortNumber,
                    outboundFileTransfer.FileName,
                    outboundFileTransfer.FileSizeInBytes,
                    outboundFileTransfer.LocalFolderPath,
                    outboundFileTransfer.RemoteFolderPath,
                    outboundFileTransfer.TransferResponseCode,
                    outboundFileTransfer.RemoteServerTransferId,
                    outboundFileTransfer.RetryCounter,
                    outboundFileTransfer.RemoteServerRetryLimit);

            var sendRequestStartedEvent = new ServerEvent
            {
                EventType = ServerEventType.RequestOutboundFileTransferStarted,
                LocalIpAddress = outboundFileTransfer.LocalServerInfo.LocalIpAddress,
                LocalPortNumber = outboundFileTransfer.LocalServerInfo.PortNumber,
                LocalFolder = outboundFileTransfer.LocalFolderPath,
                FileName = outboundFileTransfer.FileName,
                FileSizeInBytes = outboundFileTransfer.FileSizeInBytes,
                RemoteServerIpAddress = outboundFileTransfer.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = outboundFileTransfer.RemoteServerInfo.PortNumber,
                RemoteFolder = outboundFileTransfer.RemoteFolderPath,
                FileTransferId = outboundFileTransfer.Id
            };

            var sendRequestCompleteEvent = new ServerEvent
            {
                EventType = ServerEventType.RequestOutboundFileTransferComplete,
                LocalIpAddress = outboundFileTransfer.LocalServerInfo.LocalIpAddress,
                LocalPortNumber = outboundFileTransfer.LocalServerInfo.PortNumber,
                LocalFolder = outboundFileTransfer.LocalFolderPath,
                FileName = outboundFileTransfer.FileName,
                FileSizeInBytes = outboundFileTransfer.FileSizeInBytes,
                RemoteServerIpAddress = outboundFileTransfer.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = outboundFileTransfer.RemoteServerInfo.PortNumber,
                RemoteFolder = outboundFileTransfer.RemoteFolderPath,
                FileTransferId = outboundFileTransfer.Id
            };
            
            var sendRequest = await
                outboundRequest.SendServerRequestAsync(
                    requestBytes,
                    sendRequestStartedEvent,
                    sendRequestCompleteEvent).ConfigureAwait(false);

            if (sendRequest.Success) return Result.Ok();

            outboundFileTransfer.Status = FileTransferStatus.Error;
            outboundFileTransfer.ErrorMessage = sendRequest.Error;

            return sendRequest;
        }

        Result HandleFileTransferRejected(ServerRequestController inboundRequest)
        {
            var getFileTransfer = GetFileTransferByResponseCode(inboundRequest);
            if (getFileTransfer.Failure)
            {
                return getFileTransfer;
            }

            var outboundFileTransfer = getFileTransfer.Value;
            outboundFileTransfer.Status = FileTransferStatus.Rejected;
            outboundFileTransfer.ErrorMessage = "File transfer was rejected by remote server";

            inboundRequest.FileTransferId = outboundFileTransfer.Id;

            var error =
                "File transfer was rejected by remote server:" +
                Environment.NewLine + Environment.NewLine + outboundFileTransfer.OutboundRequestDetails();

            ErrorLog.Add(new ServerError(error));

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.RemoteServerRejectedFileTransfer,
                RemoteServerIpAddress = outboundFileTransfer.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = outboundFileTransfer.RemoteServerInfo.PortNumber,
                FileTransferId = outboundFileTransfer.Id,
                RequestId = inboundRequest.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            return Result.Ok();
        }

        async Task<Result> HandleFileTransferAcceptedAsync(
            ServerRequestController inboundRequest,
            CancellationToken token)
        {
            var getFileTransfer = GetFileTransferByResponseCode(inboundRequest);
            if (getFileTransfer.Failure)
            {
                return getFileTransfer;
            }

            var outboundFileTransfer = getFileTransfer.Value;
            outboundFileTransfer.Status = FileTransferStatus.Accepted;
            outboundFileTransfer.RequestId = inboundRequest.Id;
            inboundRequest.FileTransferId = outboundFileTransfer.Id;

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.RemoteServerAcceptedFileTransfer,
                RemoteServerIpAddress = outboundFileTransfer.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = outboundFileTransfer.RemoteServerInfo.PortNumber,
                FileTransferId = outboundFileTransfer.Id,
                RequestId = inboundRequest.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            var getSendSocket = inboundRequest.GetTransferSocket();
            if (getSendSocket.Failure)
            {
                outboundFileTransfer.Status = FileTransferStatus.Error;
                outboundFileTransfer.ErrorMessage = getSendSocket.Error;

                return getSendSocket;
            }

            var socket = getSendSocket.Value;

            var sendFileBytes = await
                outboundFileTransfer.SendFileAsync(
                        socket,
                        token)
                    .ConfigureAwait(false);
            
            if (sendFileBytes.Success) return Result.Ok();

            outboundFileTransfer.Status = FileTransferStatus.Error;
            outboundFileTransfer.ErrorMessage = sendFileBytes.Error;

            return sendFileBytes;
        }

        Result HandleFileTransferComplete(ServerRequestController inboundRequest)
        {
            var getFileTransfer = GetFileTransferByResponseCode(inboundRequest);
            if (getFileTransfer.Failure)
            {
                return getFileTransfer;
            }

            var outboundFileTransfer = getFileTransfer.Value;
            outboundFileTransfer.Status = FileTransferStatus.ConfirmedComplete;

            inboundRequest.FileTransferId = outboundFileTransfer.Id;

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.RemoteServerConfirmedFileTransferCompleted,
                RemoteServerIpAddress = outboundFileTransfer.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = outboundFileTransfer.RemoteServerInfo.PortNumber,
                FileTransferId = outboundFileTransfer.Id,
                RequestId = inboundRequest.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            return Result.Ok();
        }

        Result<FileTransferController> GetFileTransferByResponseCode(ServerRequestController inboundRequest)
        {
            var getResponseCode = inboundRequest.GetFileTransferResponseCode();
            if (getResponseCode.Failure)
            {
                return Result.Fail<FileTransferController>(getResponseCode.Error);
            }

            var responseCode = getResponseCode.Value;
            var remoteServerInfo = inboundRequest.RemoteServerInfo;

            var matches =
                FileTransfers.Select(t => t)
                    .Where(t => t.TransferResponseCode == responseCode)
                    .ToList();

            if (matches.Count == 0)
            {
                var error =
                    $"No file transfer was found with a response code value of {responseCode} " +
                    $"(Request received from {remoteServerInfo.SessionIpAddress}:{remoteServerInfo.PortNumber})";

                return Result.Fail<FileTransferController>(error);
            }

            return matches.Count == 1
                ? Result.Ok(matches[0])
                : Result.Fail<FileTransferController>(
                    $"Found {matches.Count} file transfers with the same response code value of {responseCode}");
        }

        public async Task<Result> GetFileAsync(
            IPAddress remoteServerIpAddress,
            int remoteServerPort,
            string remoteServerName,
            string fileName,
            long fileSizeBytes,
            string remoteFolderPath,
            string localFolderPath)
        {
            var remoteServerInfo =
                new ServerInfo(remoteServerIpAddress, remoteServerPort)
                {
                    TransferFolder = remoteFolderPath,
                    Name = remoteServerName
                };

            MyInfo.TransferFolder = localFolderPath;

            var inboundFileTransfer = new FileTransferController(_fileTransferId, _settings);
            inboundFileTransfer.EventOccurred += HandleEventOccurred;
            inboundFileTransfer.SocketEventOccurred += HandleSocketEventOccurred;
            inboundFileTransfer.FileTransferProgress += HandleFileTransferProgress;

            inboundFileTransfer.Initialize(
                FileTransferDirection.Inbound,
                FileTransferInitiator.Self,
                MyInfo,
                remoteServerInfo,
                fileName,
                fileSizeBytes,
                localFolderPath,
                remoteFolderPath);

            lock (TransferQueueLock)
            {
                FileTransfers.Add(inboundFileTransfer);
                _fileTransferId++;
            }

            var outboundRequest = new ServerRequestController(_requestId, _settings, remoteServerInfo);
            outboundRequest.EventOccurred += HandleEventOccurred;
            outboundRequest.SocketEventOccurred += HandleSocketEventOccurred;
            outboundRequest.FileTransferId = inboundFileTransfer.Id;

            inboundFileTransfer.RequestId = outboundRequest.Id;

            lock (RequestQueueLock)
            {
                Requests.Add(outboundRequest);
                _requestId++;
            }

            var requestBytes =
                ServerRequestDataBuilder.ConstructInboundFileTransferRequest(
                    MyInfo.LocalIpAddress.ToString(),
                    MyInfo.PortNumber,
                    inboundFileTransfer.Id,
                    inboundFileTransfer.FileName,
                    inboundFileTransfer.RemoteFolderPath,
                    inboundFileTransfer.LocalFolderPath);

            var sendRequestStartedEvent = new ServerEvent
            {
                EventType = ServerEventType.RequestInboundFileTransferStarted,
                RemoteServerIpAddress = inboundFileTransfer.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = inboundFileTransfer.RemoteServerInfo.PortNumber,
                RemoteFolder = inboundFileTransfer.RemoteFolderPath,
                FileName = inboundFileTransfer.FileName,
                FileSizeInBytes = inboundFileTransfer.FileSizeInBytes,
                LocalIpAddress = inboundFileTransfer.LocalServerInfo.LocalIpAddress,
                LocalPortNumber = inboundFileTransfer.LocalServerInfo.PortNumber,
                LocalFolder = inboundFileTransfer.LocalServerInfo.TransferFolder,
                FileTransferId = inboundFileTransfer.Id,
                RequestId = outboundRequest.Id
            };

            var sendRequestCompleteEvent = new ServerEvent
            {
                EventType = ServerEventType.RequestInboundFileTransferComplete,
                FileTransferId = inboundFileTransfer.Id,
                RequestId = outboundRequest.Id
            };

            var sendRequest = await
                outboundRequest.SendServerRequestAsync(
                    requestBytes,
                    sendRequestStartedEvent,
                    sendRequestCompleteEvent).ConfigureAwait(false);

            if (sendRequest.Success) return Result.Ok();

            outboundRequest.Status = ServerRequestStatus.Error;
            inboundFileTransfer.Status = FileTransferStatus.Error;
            inboundFileTransfer.ErrorMessage = sendRequest.Error;

            ErrorLog.Add(new ServerError(sendRequest.Error));

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ErrorOccurred,
                ErrorMessage = sendRequest.Error,
                FileTransferId = inboundFileTransfer.Id,
                RequestId = outboundRequest.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            return sendRequest;
        }

        Result HandleRequestedFileDoesNotExist(ServerRequestController inboundRequest)
        {
            var getFileTransferId = inboundRequest.GetRemoteServerFileTransferId();
            if (getFileTransferId.Failure)
            {
                return getFileTransferId;
            }

            var fileTransferId = getFileTransferId.Value;

            var getFileTransfer = GetFileTransferById(fileTransferId);
            if (getFileTransfer.Failure)
            {
                return getFileTransfer;
            }

            var inboundFileTransfer = getFileTransfer.Value;

            var error =
                $"Remote server rejected the request below because \"{inboundFileTransfer.FileName}\" " +
                "does not exist at the specified location:" +
                Environment.NewLine + Environment.NewLine + inboundFileTransfer.InboundRequestDetails(false);

            inboundFileTransfer.Status = FileTransferStatus.Rejected;
            inboundFileTransfer.ErrorMessage = error;

            inboundRequest.FileTransferId = inboundFileTransfer.Id;

            ErrorLog.Add(new ServerError(error));

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceivedNotificationFileDoesNotExist,
                RemoteServerIpAddress = inboundFileTransfer.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = inboundFileTransfer.RemoteServerInfo.PortNumber,
                FileTransferId = inboundFileTransfer.Id,
                RequestId = inboundRequest.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            return Result.Ok();
        }

        async Task<Result> HandleInboundFileTransferRequestAsync(ServerRequestController inboundRequest)
        {
            var getFileTransfer = GetInboundFileTransfer();
            if (getFileTransfer.Failure)
            {
                return getFileTransfer;
            }

            var inboundFileTransfer = getFileTransfer.Value;

            inboundRequest.FileTransferId = inboundFileTransfer.Id;

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceivedInboundFileTransferRequest,
                LocalFolder = inboundFileTransfer.LocalFolderPath,
                FileName = inboundFileTransfer.FileName,
                FileSizeInBytes = inboundFileTransfer.FileSizeInBytes,
                RemoteServerIpAddress = inboundFileTransfer.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = inboundFileTransfer.RemoteServerInfo.PortNumber,
                RetryCounter = inboundFileTransfer.RetryCounter,
                RemoteServerRetryLimit =  inboundFileTransfer.RemoteServerRetryLimit,
                FileTransferId = inboundFileTransfer.Id,
                RequestId = inboundRequest.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            if (!File.Exists(inboundFileTransfer.LocalFilePath)) return Result.Ok();

            var error =
                $"The request below was rejected because \"{inboundFileTransfer.FileName}\" " +
                "already exists at the specified location:" +
                Environment.NewLine + Environment.NewLine + inboundFileTransfer.InboundRequestDetails(false);

            inboundFileTransfer.Status = FileTransferStatus.Rejected;
            inboundFileTransfer.ErrorMessage = error;

            ErrorLog.Add(new ServerError(error));

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ErrorOccurred,
                ErrorMessage = error,
                FileTransferId = inboundFileTransfer.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            return await SendFileTransferResponseAsync(
                ServerRequestType.FileTransferRejected,
                inboundFileTransfer,
                ServerEventType.SendFileTransferRejectedStarted,
                ServerEventType.SendFileTransferRejectedComplete).ConfigureAwait(false);

            Result<FileTransferController> GetInboundFileTransfer()
            {
                var getFileTransferId = inboundRequest.GetInboundFileTransferId();
                if (getFileTransferId.Success)
                {
                    var fileTransferId = getFileTransferId.Value;
                    var getinboundFileTransfer = GetFileTransferById(fileTransferId);
                    if (getinboundFileTransfer.Failure)
                    {
                        return Result.Fail<FileTransferController>(getinboundFileTransfer.Error);
                    }

                    inboundFileTransfer = getinboundFileTransfer.Value;

                    var syncFileTransfer = inboundRequest.UpdateInboundFileTransfer(inboundFileTransfer);
                    if (syncFileTransfer.Failure)
                    {
                        return Result.Fail<FileTransferController>(syncFileTransfer.Error);
                    }

                    inboundFileTransfer = syncFileTransfer.Value;
                }
                else
                {
                    var getinboundFileTransfer =
                        inboundRequest.GetInboundFileTransfer(_fileTransferId, MyInfo, _settings);

                    if (getinboundFileTransfer.Failure)
                    {
                        return getinboundFileTransfer;
                    }

                    inboundFileTransfer = getinboundFileTransfer.Value;
                    inboundFileTransfer.EventOccurred += HandleEventOccurred;
                    inboundFileTransfer.SocketEventOccurred += HandleSocketEventOccurred;
                    inboundFileTransfer.FileTransferProgress += HandleFileTransferProgress;

                    lock (TransferQueueLock)
                    {
                        FileTransfers.Add(inboundFileTransfer);
                        _fileTransferId++;
                    }
                }

                return Result.Ok(inboundFileTransfer);
            }

        }

        public async Task<Result> AcceptInboundFileTransferAsync(FileTransferController inboundFileTransfer)
        {
            var acceptFileTransfer =
                await SendFileTransferResponseAsync(
                    ServerRequestType.FileTransferAccepted,
                    inboundFileTransfer,
                    ServerEventType.SendFileTransferAcceptedStarted,
                    ServerEventType.SendFileTransferAcceptedComplete).ConfigureAwait(false);

            if (acceptFileTransfer.Failure)
            {
                inboundFileTransfer.Status = FileTransferStatus.Error;
                inboundFileTransfer.ErrorMessage = acceptFileTransfer.Error;

                ErrorLog.Add(new ServerError(acceptFileTransfer.Error));

                _eventLog.Add(new ServerEvent
                {
                    EventType = ServerEventType.ErrorOccurred,
                    ErrorMessage = acceptFileTransfer.Error,
                    FileTransferId = inboundFileTransfer.Id
                });

                EventOccurred?.Invoke(this, _eventLog.Last());

                return acceptFileTransfer;
            }

            var outboundRequest = acceptFileTransfer.Value;

            var getReceiveSocket = outboundRequest.GetTransferSocket();
            if (getReceiveSocket.Failure)
            {
                inboundFileTransfer.Status = FileTransferStatus.Error;
                inboundFileTransfer.ErrorMessage = getReceiveSocket.Error;

                ErrorLog.Add(new ServerError(getReceiveSocket.Error));

                _eventLog.Add(new ServerEvent
                {
                    EventType = ServerEventType.ErrorOccurred,
                    ErrorMessage = getReceiveSocket.Error,
                    FileTransferId = inboundFileTransfer.Id
                });

                EventOccurred?.Invoke(this, _eventLog.Last());

                return getReceiveSocket;
            }

            var socket = getReceiveSocket.Value;
            var unreadBytes = outboundRequest.UnreadBytes.ToArray();

            inboundFileTransfer.Status = FileTransferStatus.Accepted;
            inboundFileTransfer.FileTransferProgress += HandleFileTransferProgress;

            var receiveFile =
                await inboundFileTransfer.ReceiveFileAsync(
                        socket,
                        unreadBytes,
                        _token)
                    .ConfigureAwait(false);

            if (receiveFile.Failure)
            {
                inboundFileTransfer.Status = FileTransferStatus.Error;
                inboundFileTransfer.ErrorMessage = receiveFile.Error;

                ErrorLog.Add(new ServerError(receiveFile.Error));

                _eventLog.Add(new ServerEvent
                {
                    EventType = ServerEventType.ErrorOccurred,
                    ErrorMessage = receiveFile.Error,
                    FileTransferId = inboundFileTransfer.Id
                });

                EventOccurred?.Invoke(this, _eventLog.Last());

                return receiveFile;
            }

            if (inboundFileTransfer.Status == FileTransferStatus.Stalled) return Result.Ok();

            var confirmFileTransfer =
                await SendFileTransferResponseAsync(
                    ServerRequestType.FileTransferComplete,
                    inboundFileTransfer,
                    ServerEventType.SendFileTransferCompletedStarted,
                    ServerEventType.SendFileTransferCompletedCompleted).ConfigureAwait(false);

            if (confirmFileTransfer.Success) return Result.Ok();
            
            inboundFileTransfer.Status = FileTransferStatus.Error;
            inboundFileTransfer.ErrorMessage = confirmFileTransfer.Error;

            ErrorLog.Add(new ServerError(confirmFileTransfer.Error));

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ErrorOccurred,
                ErrorMessage = confirmFileTransfer.Error,
                FileTransferId = inboundFileTransfer.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            return confirmFileTransfer;
        }

        public async Task<Result> RejectInboundFileTransferAsync(FileTransferController inboundFileTransfer)
        {
            inboundFileTransfer.Status = FileTransferStatus.Rejected;
            inboundFileTransfer.ErrorMessage = "File transfer was rejected by user";

            var rejectTransfer =
                await SendFileTransferResponseAsync(
                    ServerRequestType.FileTransferRejected,
                    inboundFileTransfer,
                    ServerEventType.SendFileTransferRejectedStarted,
                    ServerEventType.SendFileTransferRejectedComplete);

            if (rejectTransfer.Success) return Result.Ok();

            var inboundRequest = rejectTransfer.Value;
            inboundRequest.Status = ServerRequestStatus.Error;

            inboundFileTransfer.Status = FileTransferStatus.Error;
            inboundFileTransfer.ErrorMessage = rejectTransfer.Error;

            ErrorLog.Add(new ServerError(rejectTransfer.Error));

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ErrorOccurred,
                ErrorMessage = rejectTransfer.Error,
                FileTransferId = inboundFileTransfer.Id,
                RequestId = inboundRequest.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            return rejectTransfer;
        }

        public async Task<Result> SendNotificationFileTransferStalledAsync(int fileTransferId)
        {
            const string fileTransferStalledErrorMessage =
                "Data is no longer bring received from remote client, file transfer has been canceled (SendNotificationFileTransferStalledAsync)";

            var getFileTransfer = GetFileTransferById(fileTransferId);
            if (getFileTransfer.Failure)
            {
                ErrorLog.Add(new ServerError(getFileTransfer.Error));

                _eventLog.Add(new ServerEvent
                {
                    EventType = ServerEventType.ErrorOccurred,
                    ErrorMessage = getFileTransfer.Error
                });

                EventOccurred?.Invoke(this, _eventLog.Last());

                return getFileTransfer;
            }

            var inboundFileTransfer = getFileTransfer.Value;
            inboundFileTransfer.Status = FileTransferStatus.Stalled;
            inboundFileTransfer.ErrorMessage = fileTransferStalledErrorMessage;
            inboundFileTransfer.InboundFileTransferStalled = true;

            var notifyTransferStalled =
                await SendFileTransferResponseAsync(
                    ServerRequestType.FileTransferStalled,
                    inboundFileTransfer,
                    ServerEventType.SendFileTransferStalledStarted,
                    ServerEventType.SendFileTransferStalledComplete).ConfigureAwait(false);

            if (notifyTransferStalled.Success) return Result.Ok();

            var inboundRequest = notifyTransferStalled.Value;
            inboundRequest.Status = ServerRequestStatus.Error;

            inboundFileTransfer.Status = FileTransferStatus.Error;
            inboundFileTransfer.ErrorMessage = notifyTransferStalled.Error;

            ErrorLog.Add(new ServerError(notifyTransferStalled.Error));

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ErrorOccurred,
                ErrorMessage = notifyTransferStalled.Error,
                FileTransferId = inboundFileTransfer.Id,
                RequestId = inboundRequest.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            return notifyTransferStalled;
        }

        Result HandleFileTransferStalled(ServerRequestController inboundRequest)
        {
            const string fileTransferStalledErrorMessage =
                "Aborting file transfer, client says that data is no longer being received (HandleStalledFileTransfer)";

            var getFileTransfer = GetFileTransferByResponseCode(inboundRequest);
            if (getFileTransfer.Failure)
            {
                return getFileTransfer;
            }

            var outboundFileTransfer = getFileTransfer.Value;
            outboundFileTransfer.Status = FileTransferStatus.Cancelled;
            outboundFileTransfer.ErrorMessage = fileTransferStalledErrorMessage;
            outboundFileTransfer.OutboundFileTransferStalled = true;

            inboundRequest.FileTransferId = outboundFileTransfer.Id;

            ErrorLog.Add(new ServerError(fileTransferStalledErrorMessage));

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.FileTransferStalled,
                RemoteServerIpAddress = outboundFileTransfer.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = outboundFileTransfer.RemoteServerInfo.PortNumber,
                FileTransferId = outboundFileTransfer.Id,
                RequestId = inboundRequest.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            return Result.Ok();
        }

        public async Task<Result> RetryFileTransferAsync(int fileTransferId)
        {
            var getFileTransfer = GetFileTransferById(fileTransferId);
            if (getFileTransfer.Failure)
            {
                ErrorLog.Add(new ServerError(getFileTransfer.Error));

                _eventLog.Add(new ServerEvent
                {
                    EventType = ServerEventType.ErrorOccurred,
                    ErrorMessage = getFileTransfer.Error
                });

                EventOccurred?.Invoke(this, _eventLog.Last());

                return getFileTransfer;
            }

            var stalledFileTransfer = getFileTransfer.Value;
            FileHelper.DeleteFileIfAlreadyExists(stalledFileTransfer.LocalFilePath, 5);

            if (stalledFileTransfer.Status == FileTransferStatus.RetryLimitExceeded)
            {
                if (stalledFileTransfer.RetryLockoutExpired)
                {
                    stalledFileTransfer.ResetTransferValues();
                    stalledFileTransfer.RetryCounter = 1;

                    _eventLog.Add(new ServerEvent
                    {
                        EventType = ServerEventType.RetryLimitLockoutExpired,
                        LocalFolder = stalledFileTransfer.LocalFolderPath,
                        FileName = stalledFileTransfer.FileName,
                        FileSizeInBytes = stalledFileTransfer.FileSizeInBytes,
                        RemoteServerIpAddress = stalledFileTransfer.RemoteServerInfo.SessionIpAddress,
                        RemoteServerPortNumber = stalledFileTransfer.RemoteServerInfo.PortNumber,
                        RetryCounter = stalledFileTransfer.RetryCounter,
                        RemoteServerRetryLimit = stalledFileTransfer.RemoteServerRetryLimit,
                        RetryLockoutExpireTime = stalledFileTransfer.RetryLockoutExpireTime,
                        FileTransferId = stalledFileTransfer.Id
                    });

                    EventOccurred?.Invoke(this, _eventLog.Last());
                }
                else
                {
                    var lockoutTimeRemaining =
                        (stalledFileTransfer.RetryLockoutExpireTime - DateTime.Now).ToFormattedString();

                    var retryLimitExceeded =
                        $"Maximum # of attempts to complete stalled file transfer reached or exceeded: {Environment.NewLine}" +
                        $"File Name.................: {stalledFileTransfer.FileName}{Environment.NewLine}" +
                        $"Download Attempts.........: {stalledFileTransfer.RetryCounter}{Environment.NewLine}" +
                        $"Max Attempts Allowed......: {stalledFileTransfer.RemoteServerRetryLimit}{Environment.NewLine}" +
                        $"Current Time..............: {DateTime.Now:MM/dd/yyyy hh:mm:ss.fff tt}{Environment.NewLine}" +
                        $"Download Lockout Expires..: {stalledFileTransfer.RetryLockoutExpireTime:MM/dd/yyyy hh:mm:ss.fff tt}{Environment.NewLine}" +
                        $"Remaining Lockout Time....: {lockoutTimeRemaining}{Environment.NewLine}";

                    ErrorLog.Add(new ServerError(retryLimitExceeded));

                    _eventLog.Add(new ServerEvent
                    {
                        EventType = ServerEventType.ErrorOccurred,
                        ErrorMessage = retryLimitExceeded
                    });

                    EventOccurred?.Invoke(this, _eventLog.Last());

                    return Result.Ok();
                }
            }

            var retryTransfer =
                await SendFileTransferResponseAsync(
                    ServerRequestType.RetryOutboundFileTransfer,
                    stalledFileTransfer,
                    ServerEventType.RetryOutboundFileTransferStarted,
                    ServerEventType.RetryOutboundFileTransferComplete).ConfigureAwait(false);

            if (retryTransfer.Success) return Result.Ok();

            var inboundRequest = retryTransfer.Value;
            inboundRequest.Status = ServerRequestStatus.Error;

            stalledFileTransfer.Status = FileTransferStatus.Error;
            stalledFileTransfer.ErrorMessage = retryTransfer.Error;

            ErrorLog.Add(new ServerError(retryTransfer.Error));

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ErrorOccurred,
                ErrorMessage = retryTransfer.Error,
                FileTransferId = stalledFileTransfer.Id,
                RequestId = inboundRequest.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            return retryTransfer;
        }

        async Task<Result> HandleRetryFileTransferAsync(ServerRequestController inboundRequest)
        {
            var getFileTransfer = GetFileTransferByResponseCode(inboundRequest);
            if (getFileTransfer.Failure)
            {
                return getFileTransfer;
            }

            var canceledFileTransfer = getFileTransfer.Value;
            inboundRequest.FileTransferId = canceledFileTransfer.Id;

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceivedRetryOutboundFileTransferRequest,
                LocalFolder = Path.GetDirectoryName(canceledFileTransfer.LocalFilePath),
                FileName = Path.GetFileName(canceledFileTransfer.LocalFilePath),
                FileSizeInBytes = new FileInfo(canceledFileTransfer.LocalFilePath).Length,
                RemoteFolder = canceledFileTransfer.RemoteFolderPath,
                RemoteServerIpAddress = canceledFileTransfer.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = canceledFileTransfer.RemoteServerInfo.PortNumber,
                RetryCounter = canceledFileTransfer.RetryCounter,
                RemoteServerRetryLimit = TransferRetryLimit,
                FileTransferId = canceledFileTransfer.Id,
                RequestId = inboundRequest.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            if (canceledFileTransfer.Status == FileTransferStatus.RetryLimitExceeded)
            {
                if (canceledFileTransfer.RetryLockoutExpired)
                {
                    canceledFileTransfer.RetryCounter = 0;
                    canceledFileTransfer.ResetTransferValues();

                    _eventLog.Add(new ServerEvent
                    {
                        EventType = ServerEventType.RetryLimitLockoutExpired,
                        LocalFolder = canceledFileTransfer.LocalFolderPath,
                        FileName = canceledFileTransfer.FileName,
                        FileSizeInBytes = canceledFileTransfer.FileSizeInBytes,
                        RemoteServerIpAddress = canceledFileTransfer.RemoteServerInfo.SessionIpAddress,
                        RemoteServerPortNumber = canceledFileTransfer.RemoteServerInfo.PortNumber,
                        RetryCounter = canceledFileTransfer.RetryCounter,
                        RemoteServerRetryLimit = canceledFileTransfer.RemoteServerRetryLimit,
                        RetryLockoutExpireTime = canceledFileTransfer.RetryLockoutExpireTime,
                        FileTransferId = canceledFileTransfer.Id
                    });

                    EventOccurred?.Invoke(this, _eventLog.Last());

                    return await SendOutboundFileTransferRequestAsync(canceledFileTransfer).ConfigureAwait(false);
                }

                return await SendRetryLimitExceededAsync(canceledFileTransfer).ConfigureAwait(false);
            }

            if (canceledFileTransfer.RetryCounter >= TransferRetryLimit)
            {
                var retryLimitExceeded =
                    "Maximum # of attempts to complete stalled file transfer reached or " +
                    $"exceeded ({TransferRetryLimit} failed attempts for " +
                    $"\"{canceledFileTransfer.FileName}\"):" +
                    Environment.NewLine + Environment.NewLine + canceledFileTransfer.OutboundRequestDetails();

                canceledFileTransfer.Status = FileTransferStatus.RetryLimitExceeded;
                canceledFileTransfer.RetryLockoutExpireTime = DateTime.Now + RetryLimitLockout;
                canceledFileTransfer.ErrorMessage = "Maximum # of attempts to complete stalled file transfer reached";

                ErrorLog.Add(new ServerError(retryLimitExceeded));

                _eventLog.Add(new ServerEvent
                {
                    EventType = ServerEventType.ErrorOccurred,
                    ErrorMessage = retryLimitExceeded,
                    FileTransferId = canceledFileTransfer.Id
                });

                EventOccurred?.Invoke(this, _eventLog.Last());

                return await SendRetryLimitExceededAsync(canceledFileTransfer).ConfigureAwait(false);
            }

            canceledFileTransfer.ResetTransferValues();

            return await SendOutboundFileTransferRequestAsync(canceledFileTransfer).ConfigureAwait(false);
        }

        async Task<Result> SendRetryLimitExceededAsync(FileTransferController outboundFileTransfer)
        {
            var outboundRequest =
                new ServerRequestController(_requestId, _settings, outboundFileTransfer.RemoteServerInfo);

            outboundRequest.EventOccurred += HandleEventOccurred;
            outboundRequest.SocketEventOccurred += HandleSocketEventOccurred;
            outboundRequest.FileTransferId = outboundFileTransfer.Id;

            lock (RequestQueueLock)
            {
                Requests.Add(outboundRequest);
                _requestId++;
            }

            var requestBytes =
                ServerRequestDataBuilder.ConstructRetryLimitExceededRequest(
                    MyInfo.LocalIpAddress.ToString(),
                    MyInfo.PortNumber,
                    outboundFileTransfer.RemoteServerTransferId,
                    TransferRetryLimit,
                    outboundFileTransfer.RetryLockoutExpireTime.Ticks);

            var sendRequestStartedEvent = new ServerEvent
            {
                EventType = ServerEventType.SendRetryLimitExceededStarted,
                RemoteServerIpAddress = outboundFileTransfer.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = outboundFileTransfer.RemoteServerInfo.PortNumber,
                LocalIpAddress = outboundFileTransfer.LocalServerInfo.LocalIpAddress,
                LocalPortNumber = outboundFileTransfer.LocalServerInfo.PortNumber,
                FileName = outboundFileTransfer.FileName,
                RetryCounter = outboundFileTransfer.RetryCounter,
                RemoteServerRetryLimit = TransferRetryLimit,
                RetryLockoutExpireTime = outboundFileTransfer.RetryLockoutExpireTime,
                FileTransferId = outboundFileTransfer.Id,
                RequestId = _requestId
            };

            var sendRequestCompleteEvent = new ServerEvent
            {
                EventType = ServerEventType.SendRetryLimitExceededCompleted,
                RemoteServerIpAddress = outboundFileTransfer.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = outboundFileTransfer.RemoteServerInfo.PortNumber,
                LocalIpAddress = outboundFileTransfer.LocalServerInfo.LocalIpAddress,
                LocalPortNumber = outboundFileTransfer.LocalServerInfo.PortNumber,
                FileTransferId = outboundFileTransfer.Id,
                RequestId = _requestId
            };

            var sendRequest = await
                outboundRequest.SendServerRequestAsync(
                    requestBytes,
                    sendRequestStartedEvent,
                    sendRequestCompleteEvent);

            if (sendRequest.Success) return Result.Ok();

            outboundRequest.Status = ServerRequestStatus.Error;
            return Result.Fail(sendRequest.Error);
        }

        Result HandleRetryLimitExceeded(ServerRequestController inboundRequest)
        {
            var getFileTransferId = inboundRequest.GetRemoteServerFileTransferId();
            if (getFileTransferId.Failure)
            {
                return getFileTransferId;
            }

            var fileTransferId = getFileTransferId.Value;

            var getFileTransfer = GetFileTransferById(fileTransferId);
            if (getFileTransfer.Failure)
            {
                return getFileTransfer;
            }

            var inboundFileTransfer = getFileTransfer.Value;
            inboundFileTransfer.Status = FileTransferStatus.RetryLimitExceeded;

            inboundRequest.FileTransferId = inboundFileTransfer.Id;

            var updateFileTransfer = inboundRequest.GetRetryLockoutDetails(inboundFileTransfer);
            if (updateFileTransfer.Failure)
            {
                return updateFileTransfer;
            }

            inboundFileTransfer = updateFileTransfer.Value;

            var lockoutTimeRemaining =
                (inboundFileTransfer.RetryLockoutExpireTime - DateTime.Now).ToFormattedString();

            var retryLimitExceeded =
                $"Maximum # of attempts to complete stalled file transfer reached or exceeded: {Environment.NewLine}" +
                $"File Name.................: {inboundFileTransfer.FileName}{Environment.NewLine}" +
                $"Download Attempts.........: {inboundFileTransfer.RetryCounter}{Environment.NewLine}" +
                $"Max Attempts Allowed......: {inboundFileTransfer.RemoteServerRetryLimit}{Environment.NewLine}" +
                $"Current Time..............: {DateTime.Now:MM/dd/yyyy hh:mm:ss.fff tt}{Environment.NewLine}" +
                $"Download Lockout Expires..: {inboundFileTransfer.RetryLockoutExpireTime:MM/dd/yyyy hh:mm:ss.fff tt}{Environment.NewLine}" +
                $"Remaining Lockout Time....: {lockoutTimeRemaining}{Environment.NewLine}";

            ErrorLog.Add(new ServerError(retryLimitExceeded));

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceivedRetryLimitExceeded,
                LocalFolder = inboundFileTransfer.LocalFolderPath,
                FileName = inboundFileTransfer.FileName,
                FileSizeInBytes = inboundFileTransfer.FileSizeInBytes,
                RemoteServerIpAddress = inboundFileTransfer.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = inboundFileTransfer.RemoteServerInfo.PortNumber,
                RetryCounter = inboundFileTransfer.RetryCounter,
                RemoteServerRetryLimit = inboundFileTransfer.RemoteServerRetryLimit,
                RetryLockoutExpireTime = inboundFileTransfer.RetryLockoutExpireTime,
                FileTransferId = inboundFileTransfer.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            return Result.Ok();
        }

        async Task<Result<ServerRequestController>> SendFileTransferResponseAsync(
            ServerRequestType requestType,
            FileTransferController fileTransfer,
            ServerEventType sendRequestStartedEventType,
            ServerEventType sendRequestCompleteEventType)
        {
            var outboundRequest =
                new ServerRequestController(_requestId, _settings, fileTransfer.RemoteServerInfo);

            outboundRequest.EventOccurred += HandleEventOccurred;
            outboundRequest.SocketEventOccurred += HandleSocketEventOccurred;
            outboundRequest.FileTransferId = fileTransfer.Id;

            lock (RequestQueueLock)
            {
                Requests.Add(outboundRequest);
                _requestId++;
            }

            var requestBytes =
                ServerRequestDataBuilder.ConstructRequestWithInt64Value(
                    requestType,
                    MyInfo.LocalIpAddress.ToString(),
                    MyInfo.PortNumber,
                    fileTransfer.TransferResponseCode);

            var sendRequestStartedEvent = new ServerEvent
            {
                EventType = sendRequestStartedEventType,
                RemoteServerIpAddress = fileTransfer.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = fileTransfer.RemoteServerInfo.PortNumber,
                LocalIpAddress = MyInfo.LocalIpAddress,
                LocalPortNumber = MyInfo.PortNumber,
                FileTransferId = fileTransfer.Id,
                RequestId = outboundRequest.Id
            };

            var sendRequestCompleteEvent = new ServerEvent
            {
                EventType = sendRequestCompleteEventType,
                RemoteServerIpAddress = fileTransfer.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = fileTransfer.RemoteServerInfo.PortNumber,
                LocalIpAddress = MyInfo.LocalIpAddress,
                LocalPortNumber = MyInfo.PortNumber,
                FileTransferId = fileTransfer.Id,
                RequestId = outboundRequest.Id
            };
            
            var sendRequest = await
                outboundRequest.SendServerRequestAsync(
                    requestBytes,
                    sendRequestStartedEvent,
                    sendRequestCompleteEvent);

            if (sendRequest.Success) return Result.Ok(outboundRequest);

            outboundRequest.Status = ServerRequestStatus.Error;
            return Result.Fail<ServerRequestController>(sendRequest.Error);
        }

        async Task<Result<ServerRequestController>> SendBasicServerRequestAsync(
            IPAddress remoteServerIpAddress,
            int remoteServerPort,
            ServerRequestType requestType,
            ServerEventType sendRequestStartedEventType,
            ServerEventType sendRequestCompleteEventType)
        {
            var remoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);

            var outboundRequest =
                new ServerRequestController(_requestId, _settings, remoteServerInfo);

            outboundRequest.EventOccurred += HandleEventOccurred;
            outboundRequest.SocketEventOccurred += HandleSocketEventOccurred;

            lock (RequestQueueLock)
            {
                Requests.Add(outboundRequest);
                _requestId++;
            }

            var requestBytes =
                ServerRequestDataBuilder.ConstructBasicRequest(
                    requestType,
                    MyInfo.LocalIpAddress.ToString(),
                    MyInfo.PortNumber);

            var sendRequestStartedEvent = new ServerEvent
            {
                EventType = sendRequestStartedEventType,
                RemoteServerIpAddress = remoteServerIpAddress,
                RemoteServerPortNumber = remoteServerPort,
                LocalIpAddress = MyInfo.LocalIpAddress,
                LocalPortNumber = MyInfo.PortNumber,
                RequestId = _requestId
            };

            var sendRequestCompleteEvent = new ServerEvent
            {
                EventType = sendRequestCompleteEventType,
                RemoteServerIpAddress = remoteServerIpAddress,
                RemoteServerPortNumber = remoteServerPort,
                LocalIpAddress = MyInfo.LocalIpAddress,
                LocalPortNumber = MyInfo.PortNumber,
                RequestId = _requestId
            };

            var sendRequest = await
                outboundRequest.SendServerRequestAsync(
                    requestBytes,
                    sendRequestStartedEvent,
                    sendRequestCompleteEvent);

            if (sendRequest.Success) return Result.Ok(outboundRequest);

            outboundRequest.Status = ServerRequestStatus.Error;
            return Result.Fail<ServerRequestController>(sendRequest.Error);
        }

        public async Task<Result> RequestFileListAsync(
            IPAddress remoteServerIpAddress,
            int remoteServerPort,
            string targetFolder)
        {
            var requestBytes =
                ServerRequestDataBuilder.ConstructRequestWithStringValue(
                    ServerRequestType.FileListRequest,
                    MyInfo.LocalIpAddress.ToString(),
                    MyInfo.PortNumber,
                    targetFolder);

            var sendRequestStartedEvent = new ServerEvent
            {
                EventType = ServerEventType.RequestFileListStarted,
                LocalIpAddress = MyInfo.LocalIpAddress,
                LocalPortNumber = MyInfo.PortNumber,
                RemoteServerIpAddress = remoteServerIpAddress,
                RemoteServerPortNumber = remoteServerPort,
                RemoteFolder = targetFolder,
                RequestId = _requestId
            };

            var sendRequestCompleteEvent =
                new ServerEvent
                {
                    EventType = ServerEventType.RequestFileListComplete,
                    RequestId = _requestId
                };

            var remoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort)
            {
                TransferFolder = targetFolder
            };

            var outboundRequest =
                new ServerRequestController(_requestId, _settings, remoteServerInfo);

            outboundRequest.EventOccurred += HandleEventOccurred;
            outboundRequest.SocketEventOccurred += HandleSocketEventOccurred;

            lock (RequestQueueLock)
            {
                Requests.Add(outboundRequest);
                _requestId++;
            }

            var sendrequest =
                await outboundRequest.SendServerRequestAsync(
                    requestBytes,
                    sendRequestStartedEvent,
                    sendRequestCompleteEvent);

            if (sendrequest.Success) return Result.Ok();
            outboundRequest.Status = ServerRequestStatus.Error;
            
            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ErrorOccurred,
                ErrorMessage = sendrequest.Error,
                RequestId = outboundRequest.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            return sendrequest;
        }

        async Task<Result> HandleFileListRequestAsync(ServerRequestController inboundRequest)
        {
            var getLocalFolderPath = inboundRequest.GetLocalFolderPath();
            if (getLocalFolderPath.Failure)
            {
                return getLocalFolderPath;
            }

            MyInfo.TransferFolder = getLocalFolderPath.Value;

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceivedFileListRequest,
                RemoteServerIpAddress = inboundRequest.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = inboundRequest.RemoteServerInfo.PortNumber,
                LocalFolder = MyInfo.TransferFolder,
                RequestId = inboundRequest.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            if (!Directory.Exists(MyInfo.TransferFolder))
            {
                return
                    await SendBasicServerRequestAsync(
                        inboundRequest.RemoteServerInfo.SessionIpAddress,
                        inboundRequest.RemoteServerInfo.PortNumber,
                        ServerRequestType.RequestedFolderDoesNotExist,
                        ServerEventType.SendNotificationFolderDoesNotExistStarted,
                        ServerEventType.SendNotificationFolderDoesNotExistComplete).ConfigureAwait(false);
            }

            var fileInfoList = new FileInfoList(MyInfo.TransferFolder);
            if (fileInfoList.Count == 0)
            {
                return
                    await SendBasicServerRequestAsync(
                        inboundRequest.RemoteServerInfo.SessionIpAddress,
                        inboundRequest.RemoteServerInfo.PortNumber,
                        ServerRequestType.NoFilesAvailableForDownload,
                        ServerEventType.SendNotificationNoFilesToDownloadStarted,
                        ServerEventType.SendNotificationNoFilesToDownloadComplete).ConfigureAwait(false);
            }

            var requestBytes =
                ServerRequestDataBuilder.ConstructFileListResponse(
                    fileInfoList,
                    "*",
                    "|",
                    MyInfo.LocalIpAddress.ToString(),
                    MyInfo.PortNumber,
                    MyInfo.TransferFolder);

            var sendRequestStartedEvent = new ServerEvent
            {
                EventType = ServerEventType.SendFileListStarted,
                LocalIpAddress = MyInfo.LocalIpAddress,
                LocalPortNumber = MyInfo.PortNumber,
                RemoteServerIpAddress = inboundRequest.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = inboundRequest.RemoteServerInfo.PortNumber,
                RemoteServerFileList = fileInfoList,
                LocalFolder = MyInfo.TransferFolder,
                RequestId = _requestId
            };

            var sendRequestCompleteEvent = new ServerEvent
            {
                EventType = ServerEventType.SendFileListComplete,
                RequestId = _requestId
            };

            var outboundRequest =
                new ServerRequestController(_requestId, _settings, inboundRequest.RemoteServerInfo);

            outboundRequest.EventOccurred += HandleEventOccurred;
            outboundRequest.SocketEventOccurred += HandleSocketEventOccurred;

            lock (RequestQueueLock)
            {
                Requests.Add(outboundRequest);
                _requestId++;
            }

            return await
                outboundRequest.SendServerRequestAsync(
                    requestBytes,
                    sendRequestStartedEvent,
                    sendRequestCompleteEvent).ConfigureAwait(false);
        }

        void HandleRequestedFolderDoesNotExist(ServerRequestController inboundRequest)
        {
            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceivedNotificationFolderDoesNotExist,
                RemoteServerIpAddress = inboundRequest.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = inboundRequest.RemoteServerInfo.PortNumber,
                RequestId = inboundRequest.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());
        }

        void HandleNoFilesAvailableForDownload(ServerRequestController inboundRequest)
        {
            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceivedNotificationNoFilesToDownload,
                RemoteServerIpAddress = inboundRequest.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = inboundRequest.RemoteServerInfo.PortNumber,
                RequestId = inboundRequest.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());
        }

        void HandleFileListResponse(ServerRequestController inboundRequest)
        {
            var getFileInfoList = inboundRequest.GetFileInfoList();
            var remoteServerFileList = getFileInfoList.Value;

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceivedFileList,
                LocalIpAddress = MyInfo.LocalIpAddress,
                LocalPortNumber = MyInfo.PortNumber,
                RemoteServerIpAddress = inboundRequest.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = inboundRequest.RemoteServerInfo.PortNumber,
                RemoteFolder = inboundRequest.RemoteServerInfo.TransferFolder,
                RemoteServerFileList = remoteServerFileList,
                RequestId = inboundRequest.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());
        }

        public async Task<Result> RequestServerInfoAsync(
            IPAddress remoteServerIpAddress,
            int remoteServerPort)
        {
            var sendrequest =
                await SendBasicServerRequestAsync(
                    remoteServerIpAddress,
                    remoteServerPort,
                    ServerRequestType.ServerInfoRequest,
                    ServerEventType.RequestServerInfoStarted,
                    ServerEventType.RequestServerInfoComplete);

            if (sendrequest.Success) return Result.Ok();

            var inboundRequest = sendrequest.Value;
            inboundRequest.Status = ServerRequestStatus.Error;

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ErrorOccurred,
                ErrorMessage = sendrequest.Error,
                RequestId = inboundRequest.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            return sendrequest;
        }

        Task<Result> HandleServerInfoRequest(ServerRequestController inboundRequest)
        {
            var outboundRequest =
                new ServerRequestController(_requestId, _settings, inboundRequest.RemoteServerInfo);

            outboundRequest.EventOccurred += HandleEventOccurred;
            outboundRequest.SocketEventOccurred += HandleSocketEventOccurred;

            lock (RequestQueueLock)
            {
                Requests.Add(outboundRequest);
                _requestId++;
            }

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceivedServerInfoRequest,
                RemoteServerIpAddress = inboundRequest.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = inboundRequest.RemoteServerInfo.PortNumber,
                RequestId = inboundRequest.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            var requestBytes =
                ServerRequestDataBuilder.ConstructServerInfoResponse(
                    MyInfo.LocalIpAddress.ToString(),
                    MyInfo.PortNumber,
                    MyInfo.Platform,
                    MyInfo.PublicIpAddress.ToString(),
                    MyInfo.TransferFolder);

            var sendRequestStartedEvent = new ServerEvent
            {
                EventType = ServerEventType.SendServerInfoStarted,
                RemoteServerIpAddress = inboundRequest.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = inboundRequest.RemoteServerInfo.PortNumber,
                RemoteServerPlatform = MyInfo.Platform,
                LocalFolder = MyInfo.TransferFolder,
                LocalIpAddress = MyInfo.LocalIpAddress,
                LocalPortNumber = MyInfo.PortNumber,
                PublicIpAddress = MyInfo.PublicIpAddress,
                RequestId = _requestId
            };

            var sendRequestCompleteEvent =
                new ServerEvent
                {
                    EventType = ServerEventType.SendServerInfoComplete,
                    RequestId = _requestId
                };
            
            return
                outboundRequest.SendServerRequestAsync(
                    requestBytes,
                    sendRequestStartedEvent,
                    sendRequestCompleteEvent);
        }

        void HandleServerInfoResponse(ServerRequestController inboundRequest)
        {
            inboundRequest.RemoteServerInfo.DetermineSessionIpAddress(_settings.LocalNetworkCidrIp);

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceivedServerInfo,
                RemoteServerIpAddress = inboundRequest.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = inboundRequest.RemoteServerInfo.PortNumber,
                RemoteServerPlatform = inboundRequest.RemoteServerInfo.Platform,
                RemoteFolder = inboundRequest.RemoteServerInfo.TransferFolder,
                LocalIpAddress = inboundRequest.RemoteServerInfo.LocalIpAddress,
                PublicIpAddress = inboundRequest.RemoteServerInfo.PublicIpAddress,
                RequestId = inboundRequest.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());
        }
        
        public async Task<Result> ShutdownAsync()
        {
            if (!ServerIsListening)
            {
                return Result.Fail("Server is already shutdown");
            }

            var sendShutdownCommand =
                await SendBasicServerRequestAsync(
                    MyInfo.LocalIpAddress,
                    MyInfo.PortNumber,
                    ServerRequestType.ShutdownServerCommand,
                    ServerEventType.SendShutdownServerCommandStarted,
                    ServerEventType.SendShutdownServerCommandComplete).ConfigureAwait(false);
            
            return sendShutdownCommand.Success
                ? Result.Ok()
                : Result.Fail($"Error occurred shutting down the server + {sendShutdownCommand.Error}");
        }

        void HandleShutdownServerCommand(ServerRequestController pendingRequest)
        {
            if (MyInfo.IsEqualTo(pendingRequest.RemoteServerInfo))
            {
                ShutdownInitiated = true;
            }

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceivedShutdownServerCommand,
                RemoteServerIpAddress = pendingRequest.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = pendingRequest.RemoteServerInfo.PortNumber,
                RequestId = pendingRequest.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());
        }

        void ShutdownListenSocket()
        {
            _eventLog.Add(new ServerEvent { EventType = ServerEventType.ShutdownListenSocketStarted });
            EventOccurred?.Invoke(this, _eventLog.Last());

            try
            {
                _listenSocket.Shutdown(SocketShutdown.Both);
                _listenSocket.Close();
            }
            catch (SocketException ex)
            {
                _log.Error("Error raised in method ShutdownListenSocket", ex);
                var errorMessage = $"{ex.Message} ({ex.GetType()} raised in method AsyncFileServer.ShutdownListenSocket)";

                _eventLog.Add(new ServerEvent
                {
                    EventType = ServerEventType.ShutdownListenSocketCompletedWithError,
                    ErrorMessage = errorMessage
                });

                EventOccurred?.Invoke(this, _eventLog.Last());
            }

            _eventLog.Add(new ServerEvent { EventType = ServerEventType.ShutdownListenSocketCompletedWithoutError });
            EventOccurred?.Invoke(this, _eventLog.Last());

            _eventLog.Add(new ServerEvent { EventType = ServerEventType.ServerStoppedListening });
            EventOccurred?.Invoke(this, _eventLog.Last());
        }
    }
}