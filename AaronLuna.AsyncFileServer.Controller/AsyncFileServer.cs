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

        string _myLanCidrIp;
        int _initialized;
        int _busy;
        int _transferInProgress;
        int _shutdownInitiated;
        int _listening;
        int _textSessionId;
        int _requestId;
        int _fileTransferId;

        readonly Logger _log = new Logger(typeof(AsyncFileServer));
        readonly List<ServerEvent> _eventLog;
        readonly List<ServerRequestController> _requests;
        readonly List<FileTransferController> _fileTransfers;
        readonly List<TextSession> _textSessions;
        readonly Socket _listenSocket;

        CancellationToken _token;
        static readonly object RequestQueueLock = new object();
        static readonly object TransferQueueLock = new object();

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

        bool TransferInProgress
        {
            get => Interlocked.CompareExchange(ref _transferInProgress, 1, 1) == 1;
            set
            {
                if (value) Interlocked.CompareExchange(ref _transferInProgress, 1, 0);
                else Interlocked.CompareExchange(ref _transferInProgress, 0, 1);
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
            Settings = new ServerSettings();
            RemoteServerInfo = new ServerInfo();
            RemoteServerFileList = new FileInfoList();

            MyInfo = new ServerInfo()
            {
                TransferFolder = GetDefaultTransferFolder(),
                Name = "AsyncFileServer"
            };

            _textSessionId = 1;
            _requestId = 1;
            _fileTransferId = 1;
            _myLanCidrIp = string.Empty;
            _listenSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _eventLog = new List<ServerEvent>();
            _requests = new List<ServerRequestController>();
            _fileTransfers = new List<FileTransferController>();
            _textSessions = new List<TextSession>();
        }

        public AsyncFileServer(string name, ServerSettings settings) :this()
        {
            MyInfo = new ServerInfo
            {
                TransferFolder = settings.LocalServerFolderPath,
                Name = name
            };

            Settings = settings;
            _myLanCidrIp = settings.LocalNetworkCidrIp;
        }

        public ServerInfo MyInfo { get; set; }
        public ServerInfo RemoteServerInfo { get; set; }
        public FileInfoList RemoteServerFileList { get; set; }
        public ServerSettings Settings { get; set; }

        public SocketSettings SocketSettings => Settings.SocketSettings;
        public List<ServerInfo> RemoteServers => Settings.RemoteServers;

        public int ListenBacklogSize => SocketSettings.ListenBacklogSize;
        public int BufferSize => SocketSettings.BufferSize;
        public int SocketTimeoutInMilliseconds => SocketSettings.SocketTimeoutInMilliseconds;

        public bool IsInitialized => ServerIsInitialized;
        public bool IsListening => ServerIsListening;
        public bool IsBusy => ServerIsBusy;
        public bool FileTransferInProgress => TransferInProgress;

        public bool NoFileTransfersPending => NoFileTransfersInQueue();
        public bool FileTransferPending => PendingFileTransferInQueue();
        public int PendingFileTransferCount => PendingTransferCount();
        public Result<FileTransferController> NextPendingFileTransfer => GetNextFileTransferInQueue();
        public List<int> PendingFileTransferIds => GetPendingFileTransferIds();

        public bool NoRequests => _requestId == 1;
        public List<int> RequestIds => _requests.Select(r => r.Id).ToList();
        public int MostRecentRequestId => _requestId - 1;

        public bool NoTextSessions => NoValidTextSessions();
        public List<int> TextSessionIds => _textSessions.Select(t => t.Id).ToList();

        public int UnreadTextMessageCount => GetNumberOfUnreadTextMessages();
        public List<int> TextSessionIdsWithUnreadMessages => GetTextSessionIdsWithUnreadMessages();

        public bool NoFileTransfers => _fileTransferId == 1;
        public List<int> FileTransferIds => _fileTransfers.Select(t => t.Id).ToList();
        public int MostRecentFileTransferId => _fileTransferId - 1;

        public List<int> StalledTransferIds =>
            _fileTransfers.Select(t => t)
                .Where(t => t.TransferStalled)
                .Select(t => t.Id).ToList();

        public event EventHandler<ServerEvent> EventOccurred;
        public event EventHandler<ServerEvent> SocketEventOccurred;
        public event EventHandler<ServerEvent> FileTransferProgress;

        public override string ToString()
        {
            return string.IsNullOrEmpty(MyInfo.Name)
                ? "AsyncFileServer"
                : MyInfo.Name;
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

        bool NoFileTransfersInQueue()
        {
            return PendingTransferCount() == 0;
        }

        bool PendingFileTransferInQueue()
        {
            return PendingTransferCount() > 0;
        }

        bool QueueIsEmpty()
        {
            List<ServerRequestController> pendingRequests;

            lock (RequestQueueLock)
            {
                pendingRequests =
                    _requests.Select(r => r)
                        .Where(r => r.Status == ServerRequestStatus.Pending)
                        .ToList();
            }

            return pendingRequests.Count == 0;
        }

        int PendingTransferCount()
        {
            var pendingTransfers =
                _fileTransfers.Select(r => r)
                    .Where(r => r.TransferDirection == FileTransferDirection.Inbound
                                && r.Status == FileTransferStatus.AwaitingResponse)
                    .ToList();

            return pendingTransfers.Count;
        }

        List<int> GetPendingFileTransferIds()
        {
            return
                _fileTransfers.Select(ft => ft)
                    .Where(ft => ft.TransferDirection == FileTransferDirection.Inbound
                                 && ft.Status == FileTransferStatus.AwaitingResponse)
                    .Select(ft => ft.Id)
                    .ToList();
        }

        Result<FileTransferController> GetNextFileTransferInQueue()
        {
            var pendingTransfers =
                _fileTransfers.Select(r => r)
                    .Where(r => r.TransferDirection == FileTransferDirection.Inbound
                                && r.Status == FileTransferStatus.AwaitingResponse)
                    .ToList();

            return pendingTransfers.Count > 0
                ? Result.Ok(pendingTransfers[0])
                : Result.Fail<FileTransferController>("Queue contains no pending file transfers");
        }

        public bool NoValidTextSessions()
        {
            if (_textSessionId == 1) return true;

            var validTextSessions =
                _textSessions.Select(ts => ts).Where(ts => ts.MessageCount > 0).ToList();

            return validTextSessions.Count == 0;
        }

        public Result<TextSession> GetTextSessionById(int id)
        {
            var matches = _textSessions.Select(ts => ts).Where(ts => ts.Id == id).ToList();

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

        int GetTextSessionIdForRemoteServer(ServerInfo remoteServerInfo)
        {
            TextSession match = null;
            foreach (var textSession in _textSessions)
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

            _textSessions.Add(newTextSession);
            _textSessionId++;

            return newTextSession.Id;
        }

        public Result<ServerRequestController> GetRequestById(int id)
        {
            var matches =
                _requests.Select(r => r)
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
            var matches = _fileTransfers.Select(t => t).Where(t => t.Id == id).ToList();
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
                    .Where(e => e.FileTransferId == fileTransferId).ToList();

            eventLog.AddRange(eventsMatchingFileTransferId);

            var matchingRequests =
                _requests.Select(r => r)
                    .Where(r => r.FileTransferId == fileTransferId).ToList();

            foreach (var request in matchingRequests)
            {
                var eventsMatchingRequestId =
                    _eventLog.Select(e => e)
                        .Where(e => e.RequestId == request.Id).ToList();

                eventLog.AddRange(request.EventLog);
                eventLog.AddRange(eventsMatchingRequestId);                
            }

            var matchingFileTransfers =
                _fileTransfers.Select(t => t)
                    .Where(t => t.Id == fileTransferId).ToList();

            foreach (var fileTransfer in matchingFileTransfers)
            {
                eventLog.AddRange(fileTransfer.EventLog);
            }

            return eventLog.ApplyFilter(logLevel);
        }

        public List<ServerEvent> GetEventLogForRequest(int requestId)
        {
            var eventLog = new List<ServerEvent>();
            eventLog.AddRange(_eventLog.Select(e => e).Where(e => e.RequestId == requestId));

            var matchingRequests =
                _requests.Select(r => r)
                    .Where(r => r.Id == requestId);

            foreach (var request in matchingRequests)
            {
                eventLog.AddRange(request.EventLog);
            }

            return eventLog.ApplyFilter(LogLevel.Debug);
        }

        public List<ServerEvent> GetCompleteEventLog(LogLevel logLevel)
        {
            var eventLog = new List<ServerEvent>();
            eventLog.AddRange(_eventLog);

            foreach (var request in _requests)
            {
                eventLog.AddRange(request.EventLog);
            }

            foreach (var fileTransfer in _fileTransfers)
            {
                eventLog.AddRange(fileTransfer.EventLog);
            }

            return eventLog.ApplyFilter(logLevel);

        }

        Result<FileTransferController> GetFileTransferByResponseCode(long responseCode)
        {
            var matches = _fileTransfers.Select(t => t).Where(t => t.TransferResponseCode == responseCode).ToList();

            if (matches.Count == 0)
            {
                return Result.Fail<FileTransferController>($"No file transfer was found with a response code value of {responseCode}");
            }

            if (matches.Count > 1)
            {
                return Result.Fail<FileTransferController>($"Found {matches.Count} file transfers with the same response code value of {responseCode}");
            }
            
            return Result.Ok(matches[0]);
        }

        int GetNumberOfUnreadTextMessages()
        {
            var unreadCount = 0;
            foreach (var textSession in _textSessions)
            {
                foreach (var textMessage in textSession.Messages)
                {
                    if (textMessage.Unread)
                    {
                        unreadCount++;
                    }
                }
            }

            return unreadCount;
        }

        List<int> GetTextSessionIdsWithUnreadMessages()
        {
            var sessionIds = new List<int>();
            foreach (var textSession in _textSessions)
            {
                foreach (var textMessage in textSession.Messages)
                {
                    if (textMessage.Unread)
                    {
                        sessionIds.Add(textSession.Id);
                    }
                }
            }

            return sessionIds.Distinct().ToList();
        }

        public async Task InitializeAsync(string cidrIp, int port)
        {
            if (ServerIsInitialized) return;

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

            _myLanCidrIp = cidrIp;
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

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = ServerEventType.ServerStartedListening,
                    LocalPortNumber = MyInfo.PortNumber
                });

            return Result.Ok();
        }

        async Task<Result> HandleIncomingRequestsAsync()
        {
            // Main loop. Server handles incoming connections until shutdown command is received
            // or an error is encountered
            while (true)
            {
                if (FileTransferPending)
                {
                    EventOccurred?.Invoke(this,
                        new ServerEvent
                        {
                            EventType = ServerEventType.QueueContainsUnhandledRequests,
                            ItemsInQueueCount = PendingFileTransferCount
                        });
                }

                var acceptConnection = await _listenSocket.AcceptTaskAsync(_token).ConfigureAwait(false);
                if (acceptConnection.Failure)
                {
                    return acceptConnection;
                }

                var socket = acceptConnection.Value;
                var remoteServerIpString = socket.RemoteEndPoint.ToString().Split(':')[0];
                var remoteServerIpAddress = NetworkUtilities.ParseSingleIPv4Address(remoteServerIpString).Value;

                EventOccurred?.Invoke(this,
                    new ServerEvent
                    {
                        EventType = ServerEventType.ConnectionAccepted,
                        RemoteServerIpAddress = remoteServerIpAddress
                    });

                var inboundRequest =
                    new ServerRequestController(_requestId, Settings);

                inboundRequest.EventOccurred += HandleEventOccurred;
                inboundRequest.SocketEventOccurred += HandleSocketEventOccurred;

                var receiveRequest = await inboundRequest.ReceiveServerRequestAsync(socket).ConfigureAwait(false);
                if (receiveRequest.Failure)
                {
                    return receiveRequest;
                }

                lock (RequestQueueLock)
                {
                    _requests.Add(inboundRequest);
                    _requestId++;
                }

                if (_token.IsCancellationRequested || ShutdownInitiated) return Result.Ok();
                if (ServerIsBusy) continue;

                var processRequest = await ProcessRequestAsync(inboundRequest).ConfigureAwait(false);
                if (processRequest.Failure)
                {
                    return processRequest;
                }
            }
        }

        public async Task<Result> ProcessNextFileTransferInQueueAsync()
        {
            if (NoFileTransfersPending)
            {
                return Result.Fail("Queue is empty");
            }

            if (ServerIsBusy)
            {
                return Result.Fail("Server is busy, please try again after the current operation has completed");
            }

            var getPendingTransfer = GetNextFileTransferInQueue();
            if (getPendingTransfer.Failure)
            {
                return getPendingTransfer;
            }

            var getServerRequest = GetRequestById(getPendingTransfer.Value.Id);

            return await ProcessRequestAsync(getServerRequest.Value).ConfigureAwait(false);
        }

        async Task<Result> ProcessRequestAsync(ServerRequestController inboundRequest)
        {
            ServerIsBusy = true;
            RemoteServerInfo = inboundRequest.RemoteServerInfo;
            RemoteServerInfo.TransferFolder = inboundRequest.RemoteServerInfo.TransferFolder;

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ProcessRequestStarted,
                RequestType = inboundRequest.RequestType,
                RequestId = inboundRequest.Id,
                RemoteServerIpAddress = RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = RemoteServerInfo.PortNumber
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            var result = await ProcessRequestTypeAsync(inboundRequest);

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ProcessRequestComplete,
                RequestType = inboundRequest.RequestType,
                RequestId = inboundRequest.Id,
                RemoteServerIpAddress = RemoteServerInfo.SessionIpAddress
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            inboundRequest.Status = ServerRequestStatus.Processed;
            inboundRequest.ShutdownSocket();

            ServerIsBusy = false;

            if (result.Success) return await HandleRemainingRequestsInQueueAsync().ConfigureAwait(false);

            inboundRequest.Status = ServerRequestStatus.Error;

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ErrorOccurred,
                ErrorMessage = result.Error,
                RequestId = inboundRequest.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            return result;
        }

        async Task<Result> ProcessRequestTypeAsync(ServerRequestController inboundRequest)
        {
            var result = Result.Ok();

            switch (inboundRequest.RequestType)
            {
                case ServerRequestType.TextMessage:
                    result = ReceiveTextMessage(inboundRequest);
                    break;

                case ServerRequestType.InboundFileTransferRequest:
                    result = await HandleInboundFileTransferRequestAsync(inboundRequest).ConfigureAwait(false);
                    break;

                case ServerRequestType.OutboundFileTransferRequest:
                    result = await HandleOutboundFileTransferRequestAsync(inboundRequest).ConfigureAwait(false);
                    break;

                case ServerRequestType.RequestedFileDoesNotExist:
                    result = HandleRequestedFileDoesNotExist(inboundRequest);
                    break;

                case ServerRequestType.FileTransferRejected:
                    result = HandleFileTransferRejected(inboundRequest);
                    break;

                case ServerRequestType.FileTransferAccepted:
                    result = await HandleFileTransferAcceptedAsync(inboundRequest, _token).ConfigureAwait(false);
                    break;

                case ServerRequestType.FileTransferStalled:
                    result = HandleStalledFileTransfer(inboundRequest);
                    break;

                case ServerRequestType.FileTransferComplete:
                    result = HandleFileTransferCompleted(inboundRequest);
                    break;

                case ServerRequestType.RetryOutboundFileTransfer:
                    result = await HandleRetryFileTransferAsync(inboundRequest).ConfigureAwait(false);
                    break;

                case ServerRequestType.RetryLimitExceeded:
                    result = HandleRetryLimitExceeded(inboundRequest);
                    break;

                case ServerRequestType.FileListRequest:
                    result = await SendFileListAsync(inboundRequest).ConfigureAwait(false);
                    break;

                case ServerRequestType.FileListResponse:
                    ReceiveFileList(inboundRequest);
                    break;

                case ServerRequestType.NoFilesAvailableForDownload:
                    HandleNoFilesAvailableForDownload(inboundRequest);
                    break;

                case ServerRequestType.RequestedFolderDoesNotExist:
                    HandleRequestedFolderDoesNotExist(inboundRequest);
                    break;

                case ServerRequestType.ServerInfoRequest:
                    result = await SendServerInfoAsync(inboundRequest).ConfigureAwait(false);
                    break;

                case ServerRequestType.ServerInfoResponse:
                    ReceiveServerInfo(inboundRequest);
                    break;

                case ServerRequestType.ShutdownServerCommand:
                    HandleShutdownServerCommand(inboundRequest);
                    break;

                default:
                    var error = $"Unable to determine request type, value of '{inboundRequest.RequestType}' is invalid.";
                    return Result.Fail(error);
            }

            return result;
        }

        void HandleEventOccurred(object sender, ServerEvent e)
        {
            EventOccurred?.Invoke(sender, e);
        }

        void HandleSocketEventOccurred(object sender, ServerEvent e)
        {
            SocketEventOccurred?.Invoke(sender, e);
        }

        void HandleFileTransferProgress(object sender, ServerEvent e)
        {
            FileTransferProgress?.Invoke(sender, e);
        }

        async Task<Result> HandleRemainingRequestsInQueueAsync()
        {
            if (QueueIsEmpty()) return Result.Ok();

            foreach (var request in _requests.Select(r => r).Where(r => r.Status == ServerRequestStatus.Pending))
            {
                var result = await ProcessRequestAsync(request).ConfigureAwait(false);
                if (result.Failure)
                {
                    return result;
                }
            }

            return Result.Ok();
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

            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);
            var textSessionId = GetTextSessionIdForRemoteServer(RemoteServerInfo);

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
                RemoteServerPortNumber = remoteServerPort
            };

            var sendRequestCompleteEvent =
                new ServerEvent {EventType = ServerEventType.SendTextMessageComplete};

            var outboundRequest =
                new ServerRequestController(_requestId, Settings)
                {
                    RemoteServerInfo = RemoteServerInfo
                };

            outboundRequest.EventOccurred += HandleEventOccurred;
            outboundRequest.SocketEventOccurred += HandleSocketEventOccurred;

            lock (RequestQueueLock)
            {
                _requests.Add(outboundRequest);
                _requestId++;
            }

            var sendRequest =
                await outboundRequest.SendServerRequestAsync(
                    requestBytes,
                    remoteServerIpAddress,
                    remoteServerPort,
                    sendRequestStartEvent,
                    sendRequestCompleteEvent).ConfigureAwait(false);

            if (sendRequest.Failure)
            {
                outboundRequest.Status = ServerRequestStatus.Error;

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

        public async Task<Result> SendFileAsync(
            IPAddress remoteServerIpAddress,
            int remoteServerPort,
            string remoteServerName,
            string localFilePath,
            string remoteFolderPath)
        {
            if (!File.Exists(localFilePath))
            {
                return Result.Fail("File does not exist: " + localFilePath);
            }

            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort)
            {
                Name = remoteServerName
            };

            var outboundFileTransfer =
                new FileTransferController(
                    _fileTransferId, Settings);

            outboundFileTransfer.EventOccurred += HandleEventOccurred;
            outboundFileTransfer.SocketEventOccurred += HandleSocketEventOccurred;
            outboundFileTransfer.FileTransferProgress += HandleFileTransferProgress;

            outboundFileTransfer.InitializeOutboundFileTransfer(
                FileTransferInitiator.Self,
                MyInfo,
                RemoteServerInfo,
                localFilePath,
                remoteFolderPath);

            lock (TransferQueueLock)
            {
                _fileTransfers.Add(outboundFileTransfer);
                _fileTransferId++;
            }

            return await SendOutboundFileTransferRequestAsync(outboundFileTransfer).ConfigureAwait(false);
        }

        async Task<Result> HandleOutboundFileTransferRequestAsync(ServerRequestController inboundRequest)
        {
            var getFileTransfer =
                inboundRequest.GetOutboundFileTransfer(_fileTransferId, MyInfo, Settings);

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
                _fileTransfers.Add(outboundFileTransfer);
                _fileTransferId++;
            }

            if (!File.Exists(outboundFileTransfer.LocalFilePath))
            {
                return await SendFileTransferResponseAsync(
                    ServerRequestType.RequestedFileDoesNotExist,
                    outboundFileTransfer.RemoteServerTransferId,
                    outboundFileTransfer.RemoteServerTransferId,
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
                RequestId = inboundRequest.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            return await SendOutboundFileTransferRequestAsync(outboundFileTransfer).ConfigureAwait(false);
        }

        async Task<Result> SendOutboundFileTransferRequestAsync(FileTransferController outboundFileTransfer)
        {
            var requestBytes =
                ServerRequestDataBuilder.ConstructOutboundFileTransferRequest(
                    MyInfo.LocalIpAddress.ToString(),
                    MyInfo.PortNumber,
                    outboundFileTransfer.TransferResponseCode,
                    outboundFileTransfer.RemoteServerTransferId,
                    outboundFileTransfer.RetryCounter,
                    outboundFileTransfer.RemoteServerRetryLimit,
                    outboundFileTransfer.LocalFilePath,
                    outboundFileTransfer.FileSizeInBytes,
                    outboundFileTransfer.RemoteFolderPath);

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

            var outboundRequest =
                new ServerRequestController(_requestId, Settings)
                {
                    RemoteServerInfo = RemoteServerInfo
                };

            outboundRequest.EventOccurred += HandleEventOccurred;
            outboundRequest.SocketEventOccurred += HandleSocketEventOccurred;
            outboundRequest.FileTransferId = outboundFileTransfer.Id;

            lock (RequestQueueLock)
            {
                _requests.Add(outboundRequest);
                _requestId++;
            }

            var sendRequest =
                await outboundRequest.SendServerRequestAsync(
                    requestBytes,
                    outboundFileTransfer.RemoteServerInfo.SessionIpAddress,
                    outboundFileTransfer.RemoteServerInfo.PortNumber,
                    sendRequestStartedEvent,
                    sendRequestCompleteEvent).ConfigureAwait(false);

            if (sendRequest.Success) return Result.Ok();

            outboundRequest.Status = ServerRequestStatus.Error;
            outboundFileTransfer.Status = FileTransferStatus.Error;
            outboundFileTransfer.ErrorMessage = sendRequest.Error;

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ErrorOccurred,
                ErrorMessage = sendRequest.Error,
                FileTransferId = outboundFileTransfer.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            return sendRequest;
        }

        Result HandleFileTransferRejected(ServerRequestController inboundRequest)
        {
            var getResponseCode = inboundRequest.GetFileTransferResponseCode();
            if (getResponseCode.Failure)
            {
                return getResponseCode;
            }

            var responseCode = getResponseCode.Value;

            var getFileTransfer = GetFileTransferByResponseCode(responseCode);
            if (getFileTransfer.Failure)
            {
                return getFileTransfer;
            }

            var outboundFileTransfer = getFileTransfer.Value;
            outboundFileTransfer.Status = FileTransferStatus.Rejected;
            outboundFileTransfer.TransferStartTime = DateTime.Now;
            outboundFileTransfer.TransferCompleteTime = outboundFileTransfer.TransferStartTime;
            inboundRequest.FileTransferId = outboundFileTransfer.Id;

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.RemoteServerRejectedFileTransfer,
                RemoteServerIpAddress = outboundFileTransfer.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = outboundFileTransfer.RemoteServerInfo.PortNumber,
                FileTransferId = outboundFileTransfer.Id,
                RequestId = inboundRequest.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());
            TransferInProgress = false;

            return Result.Ok();
        }

        async Task<Result> HandleFileTransferAcceptedAsync(
            ServerRequestController inboundRequest,
            CancellationToken token)
        {
            TransferInProgress = true;

            var getResponseCode = inboundRequest.GetFileTransferResponseCode();
            if (getResponseCode.Failure)
            {
                return getResponseCode;
            }

            var responseCode = getResponseCode.Value;

            var getFileTransfer = GetFileTransferByResponseCode(responseCode);
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
                return getSendSocket;
            }

            var socket = getSendSocket.Value;

            var sendFileBytes =
                await outboundFileTransfer.SendFileBytesAsync(
                        socket,
                        token)
                    .ConfigureAwait(false);

            TransferInProgress = false;
            if (sendFileBytes.Success) return Result.Ok();

            outboundFileTransfer.Status = FileTransferStatus.Error;
            outboundFileTransfer.ErrorMessage = sendFileBytes.Error;
            outboundFileTransfer.TransferCompleteTime = DateTime.Now;

            return sendFileBytes;
        }

        Result HandleFileTransferCompleted(ServerRequestController inboundRequest)
        {
            var getResponseCode = inboundRequest.GetFileTransferResponseCode();
            if (getResponseCode.Failure)
            {
                return getResponseCode;
            }

            var responseCode = getResponseCode.Value;

            var getFileTransfer = GetFileTransferByResponseCode(responseCode);
            if (getFileTransfer.Failure)
            {
                return getFileTransfer;
            }

            var outboundFileTransfer = getFileTransfer.Value;
            outboundFileTransfer.Status = FileTransferStatus.Complete;

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

        public async Task<Result> GetFileAsync(
            IPAddress remoteServerIpAddress,
            int remoteServerPort,
            string remoteServerName,
            string remoteFilePath,
            long fileSizeBytes,
            string localFolderPath)
        {
            var fileName = Path.GetFileName(remoteFilePath);
            var remoteFolderPath = Path.GetDirectoryName(remoteFilePath);

            RemoteServerInfo =
                new ServerInfo(remoteServerIpAddress, remoteServerPort)
                {
                    TransferFolder = remoteFolderPath,
                    Name = remoteServerName
                };

            MyInfo.TransferFolder = localFolderPath;

            var inboundFileTransfer =
                new FileTransferController(
                    _fileTransferId, Settings);

            inboundFileTransfer.EventOccurred += HandleEventOccurred;
            inboundFileTransfer.SocketEventOccurred += HandleSocketEventOccurred;
            inboundFileTransfer.FileTransferProgress += HandleFileTransferProgress;

            inboundFileTransfer.InitializeInboundFileTransfer(
                FileTransferInitiator.Self,
                MyInfo,
                RemoteServerInfo,
                fileName,
                fileSizeBytes,
                localFolderPath,
                remoteFolderPath);

            lock (TransferQueueLock)
            {
                _fileTransfers.Add(inboundFileTransfer);
                _fileTransferId++;
            }

            var requestBytes =
                ServerRequestDataBuilder.ConstructInboundFileTransferRequest(
                    MyInfo.LocalIpAddress.ToString(),
                    MyInfo.PortNumber,
                    inboundFileTransfer.Id,
                    inboundFileTransfer.RemoteFilePath,
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
                FileTransferId = inboundFileTransfer.Id
            };

            var sendRequestCompleteEvent = new ServerEvent
            {
                EventType = ServerEventType.RequestInboundFileTransferComplete,
                FileTransferId = inboundFileTransfer.Id
            };

            var outboundRequest =
                new ServerRequestController(BufferSize, Settings)
                {
                    RemoteServerInfo = RemoteServerInfo
                };

            outboundRequest.EventOccurred += HandleEventOccurred;
            outboundRequest.SocketEventOccurred += HandleSocketEventOccurred;
            outboundRequest.FileTransferId = inboundFileTransfer.Id;

            lock (RequestQueueLock)
            {
                _requests.Add(outboundRequest);
                _requestId++;
            }

            var sendRequest =
                await outboundRequest.SendServerRequestAsync(
                    requestBytes,
                    remoteServerIpAddress,
                    remoteServerPort,
                    sendRequestStartedEvent,
                    sendRequestCompleteEvent).ConfigureAwait(false);

            if (sendRequest.Success) return Result.Ok();

            outboundRequest.Status = ServerRequestStatus.Error;
            inboundFileTransfer.Status = FileTransferStatus.Error;
            inboundFileTransfer.ErrorMessage = sendRequest.Error;

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ErrorOccurred,
                ErrorMessage = sendRequest.Error,
                FileTransferId = inboundFileTransfer.Id
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
            inboundFileTransfer.Status = FileTransferStatus.Rejected;
            inboundFileTransfer.TransferStartTime = DateTime.Now;
            inboundFileTransfer.TransferCompleteTime = inboundFileTransfer.TransferStartTime;

            inboundRequest.FileTransferId = inboundFileTransfer.Id;

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceivedNotificationFileDoesNotExist,
                RemoteServerIpAddress = inboundFileTransfer.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = inboundFileTransfer.RemoteServerInfo.PortNumber,
                FileTransferId = inboundFileTransfer.Id,
                RequestId = inboundRequest.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());
            TransferInProgress = false;

            return Result.Ok();
        }

        async Task<Result> HandleInboundFileTransferRequestAsync(ServerRequestController inboundRequest)
        {
            var getFileTransfer = GetInboundFileTransfer(inboundRequest);
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

            if (File.Exists(inboundFileTransfer.LocalFilePath))
            {
                inboundFileTransfer.Status = FileTransferStatus.Rejected;
                inboundFileTransfer.TransferStartTime = DateTime.Now;
                inboundFileTransfer.TransferCompleteTime = inboundFileTransfer.TransferStartTime;

                return await SendFileTransferResponseAsync(
                        ServerRequestType.FileTransferRejected,
                        inboundFileTransfer.Id,
                        inboundFileTransfer.TransferResponseCode,
                        ServerEventType.SendFileTransferRejectedStarted,
                        ServerEventType.SendFileTransferRejectedComplete).ConfigureAwait(false);
            }

            return Result.Ok();
        }

        Result<FileTransferController> GetInboundFileTransfer(ServerRequestController inboundRequest)
        {
            FileTransferController inboundFileTransfer;
            var getFileTransferId = inboundRequest.GetInboundFileTransferId();
            if (getFileTransferId.Success)
            {
                var fileTransferId = getFileTransferId.Value;
                var getFileTransfer = GetFileTransferById(fileTransferId);
                if (getFileTransfer.Failure)
                {
                    return Result.Fail<FileTransferController>(getFileTransfer.Error);
                }

                inboundFileTransfer = getFileTransfer.Value;

                var syncFileTransfer = inboundRequest.UpdateInboundFileTransfer(inboundFileTransfer);
                if (syncFileTransfer.Failure)
                {
                    return Result.Fail<FileTransferController>(syncFileTransfer.Error);
                }

                inboundFileTransfer = syncFileTransfer.Value;
            }
            else
            {
                var getFileTransfer =
                    inboundRequest.GetInboundFileTransfer(_fileTransferId, MyInfo, Settings);

                if (getFileTransfer.Failure)
                {
                    return getFileTransfer;
                }

                inboundFileTransfer = getFileTransfer.Value;
                inboundFileTransfer.EventOccurred += HandleEventOccurred;
                inboundFileTransfer.SocketEventOccurred += HandleSocketEventOccurred;
                inboundFileTransfer.FileTransferProgress += HandleFileTransferProgress;

                lock (TransferQueueLock)
                {
                    _fileTransfers.Add(inboundFileTransfer);
                    _fileTransferId++;
                }
            }

            return Result.Ok(inboundFileTransfer);
        }

        public async Task<Result> AcceptInboundFileTransferAsync(FileTransferController inboundFileTransfer)
        {
            var getServerRequest = GetRequestById(inboundFileTransfer.RequestId);
            if (getServerRequest.Failure)
            {
                return getServerRequest;
            }

            var inboundRequest = getServerRequest.Value;

            var acceptFileTransfer =
                await AcceptFileTransferAsync(
                    inboundRequest,
                    inboundFileTransfer.Id,
                    inboundFileTransfer.TransferResponseCode,
                    ServerEventType.SendFileTransferAcceptedStarted,
                    ServerEventType.SendFileTransferAcceptedComplete).ConfigureAwait(false);

            if (acceptFileTransfer.Failure)
            {
                inboundRequest.Status = ServerRequestStatus.Error;
                inboundFileTransfer.Status = FileTransferStatus.Error;
                inboundFileTransfer.ErrorMessage = acceptFileTransfer.Error;

                _eventLog.Add(new ServerEvent
                {
                    EventType = ServerEventType.ErrorOccurred,
                    ErrorMessage = acceptFileTransfer.Error,
                    FileTransferId = inboundFileTransfer.Id
                });

                EventOccurred?.Invoke(this, _eventLog.Last());

                return acceptFileTransfer;
            }

            inboundFileTransfer.Status = FileTransferStatus.Accepted;
            inboundFileTransfer.FileTransferProgress += HandleFileTransferProgress;

            var getReceiveSocket = inboundRequest.GetTransferSocket();
            if (getReceiveSocket.Failure)
            {
                return getReceiveSocket;
            }

            var socket = getReceiveSocket.Value;
            var unreadBytes = inboundRequest.UnreadBytes.ToArray();

            var receiveFile =
                await inboundFileTransfer.ReceiveFileAsync(
                        socket,
                        unreadBytes,
                        _token)
                    .ConfigureAwait(false);

            if (receiveFile.Failure)
            {
                inboundRequest.Status = ServerRequestStatus.Error;
                inboundFileTransfer.Status = FileTransferStatus.Error;
                inboundFileTransfer.ErrorMessage = receiveFile.Error;

                _eventLog.Add(new ServerEvent
                {
                    EventType = ServerEventType.ErrorOccurred,
                    ErrorMessage = receiveFile.Error,
                    FileTransferId = inboundFileTransfer.Id
                });

                EventOccurred?.Invoke(this, _eventLog.Last());

                return receiveFile;
            }

            var confirmFileTransfer =
                await SendFileTransferResponseAsync(
                    ServerRequestType.FileTransferComplete,
                    inboundFileTransfer.Id,
                    inboundFileTransfer.TransferResponseCode,
                    ServerEventType.SendFileTransferCompletedStarted,
                    ServerEventType.SendFileTransferCompletedCompleted).ConfigureAwait(false);

            if (confirmFileTransfer.Success) return Result.Ok();

            inboundRequest.Status = ServerRequestStatus.Error;
            inboundFileTransfer.Status = FileTransferStatus.Error;
            inboundFileTransfer.ErrorMessage = confirmFileTransfer.Error;

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ErrorOccurred,
                ErrorMessage = confirmFileTransfer.Error,
                FileTransferId = inboundFileTransfer.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            return confirmFileTransfer;
        }

        Task<Result> AcceptFileTransferAsync(
            ServerRequestController inboundRequest,
            int fileTransferId,
            long responseCode,
            ServerEventType sendRequestStartedEventType,
            ServerEventType sendRequestCompleteEventType)
        {
            var requestBytes =
                ServerRequestDataBuilder.ConstructRequestWithInt64Value(
                    ServerRequestType.FileTransferAccepted,
                    MyInfo.LocalIpAddress.ToString(),
                    MyInfo.PortNumber,
                    responseCode);

            var sendRequestStartedEvent = new ServerEvent
            {
                EventType = sendRequestStartedEventType,
                RemoteServerIpAddress = RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = RemoteServerInfo.PortNumber,
                LocalIpAddress = MyInfo.LocalIpAddress,
                LocalPortNumber = MyInfo.PortNumber,
                FileTransferId = fileTransferId
            };

            var sendRequestCompleteEvent = new ServerEvent
            {
                EventType = sendRequestCompleteEventType,
                RemoteServerIpAddress = RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = RemoteServerInfo.PortNumber,
                LocalIpAddress = MyInfo.LocalIpAddress,
                LocalPortNumber = MyInfo.PortNumber,
                FileTransferId = fileTransferId
            };

            return
                inboundRequest.SendServerRequestAsync(
                    requestBytes,
                    RemoteServerInfo.SessionIpAddress,
                    RemoteServerInfo.PortNumber,
                    sendRequestStartedEvent,
                    sendRequestCompleteEvent);
        }

        public async Task<Result> RejectInboundFileTransferAsync(FileTransferController inboundFileTransfer)
        {
            inboundFileTransfer.Status = FileTransferStatus.Rejected;
            inboundFileTransfer.TransferStartTime = DateTime.Now;
            inboundFileTransfer.TransferCompleteTime = inboundFileTransfer.TransferStartTime;

            var rejectTransfer =
                await SendFileTransferResponseAsync(
                    ServerRequestType.FileTransferRejected,
                    inboundFileTransfer.Id,
                    inboundFileTransfer.TransferResponseCode,
                    ServerEventType.SendFileTransferRejectedStarted,
                    ServerEventType.SendFileTransferRejectedComplete);

            if (rejectTransfer.Success) return Result.Ok();

            var inboundRequest = _requests.Last();
            inboundRequest.Status = ServerRequestStatus.Error;

            inboundFileTransfer.Status = FileTransferStatus.Error;
            inboundFileTransfer.ErrorMessage = rejectTransfer.Error;

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
                return getFileTransfer;
            }

            var inboundFileTransfer = getFileTransfer.Value;
            inboundFileTransfer.Status = FileTransferStatus.Stalled;
            inboundFileTransfer.TransferCompleteTime = DateTime.Now;
            inboundFileTransfer.ErrorMessage = fileTransferStalledErrorMessage;
            inboundFileTransfer.InboundFileTransferStalled = true;

            var notifyTransferStalled =
                await SendFileTransferResponseAsync(
                    ServerRequestType.FileTransferStalled,
                    inboundFileTransfer.Id,
                    inboundFileTransfer.TransferResponseCode,
                    ServerEventType.SendFileTransferStalledStarted,
                    ServerEventType.SendFileTransferStalledComplete).ConfigureAwait(false);

            if (notifyTransferStalled.Success) return Result.Ok();

            var inboundRequest = _requests.Last();
            inboundRequest.Status = ServerRequestStatus.Error;

            inboundFileTransfer.Status = FileTransferStatus.Error;
            inboundFileTransfer.ErrorMessage = notifyTransferStalled.Error;

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

        Result HandleStalledFileTransfer(ServerRequestController inboundRequest)
        {
            const string fileTransferStalledErrorMessage =
                "Aborting file transfer, client says that data is no longer being received (HandleStalledFileTransfer)";

            var getResponseCode = inboundRequest.GetFileTransferResponseCode();
            if (getResponseCode.Failure)
            {
                return getResponseCode;
            }

            var responseCode = getResponseCode.Value;

            var getFileTransfer = GetFileTransferByResponseCode(responseCode);
            if (getFileTransfer.Failure)
            {
                return getFileTransfer;
            }

            var outboundFileTransfer = getFileTransfer.Value;
            outboundFileTransfer.Status = FileTransferStatus.Cancelled;
            outboundFileTransfer.TransferCompleteTime = DateTime.Now;
            outboundFileTransfer.ErrorMessage = fileTransferStalledErrorMessage;
            outboundFileTransfer.OutboundFileTransferStalled = true;

            inboundRequest.FileTransferId = outboundFileTransfer.Id;

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

        public async Task<Result> RetryFileTransferAsync(
            int fileTransferId,
            IPAddress remoteServerIpAddress,
            int remoteServerPort)
        {
            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);

            var getFileTransferResult = GetFileTransferById(fileTransferId);
            if (getFileTransferResult.Failure)
            {
                var error = $"{getFileTransferResult.Error} (AsyncFileServer.RetryFileTransferAsync)";
                return Result.Fail(error);
            }

            var stalledFileTransfer = getFileTransferResult.Value;

            var retryTransfer =
                await SendFileTransferResponseAsync(
                    ServerRequestType.RetryOutboundFileTransfer,
                    stalledFileTransfer.Id,
                    stalledFileTransfer.TransferResponseCode,
                    ServerEventType.RetryOutboundFileTransferStarted,
                    ServerEventType.RetryOutboundFileTransferComplete).ConfigureAwait(false);

            if (retryTransfer.Success) return Result.Ok();

            var inboundRequest = _requests.Last();
            inboundRequest.Status = ServerRequestStatus.Error;

            stalledFileTransfer.Status = FileTransferStatus.Error;
            stalledFileTransfer.ErrorMessage = retryTransfer.Error;

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
            var getResponseCode = inboundRequest.GetFileTransferResponseCode();
            if (getResponseCode.Failure)
            {
                return getResponseCode;
            }

            var responseCode = getResponseCode.Value;

            var getFileTransfer = GetFileTransferByResponseCode(responseCode);
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
                RemoteServerRetryLimit = Settings.TransferRetryLimit,
                FileTransferId = canceledFileTransfer.Id,
                RequestId = inboundRequest.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            if (canceledFileTransfer.RetryCounter >= Settings.TransferRetryLimit)
            {
                var retryLImitExceeded =
                    $"{Environment.NewLine}Maximum # of attempts to complete stalled file transfer reached or exceeded: " +
                    $"({Settings.TransferRetryLimit} failed attempts for \"{Path.GetFileName(canceledFileTransfer.LocalFilePath)}\")";

                canceledFileTransfer.Status = FileTransferStatus.RetryLimitExceeded;
                canceledFileTransfer.RetryLockoutExpireTime = DateTime.Now + Settings.RetryLimitLockout;
                canceledFileTransfer.ErrorMessage = retryLImitExceeded;

                return await SendRetryLimitExceededAsync(canceledFileTransfer).ConfigureAwait(false);
            }

            canceledFileTransfer.ResetTransferValues();
            canceledFileTransfer.RemoteServerRetryLimit = Settings.TransferRetryLimit;

            return await SendOutboundFileTransferRequestAsync(canceledFileTransfer).ConfigureAwait(false);
        }

        Task<Result> SendRetryLimitExceededAsync(FileTransferController outboundFileTransfer)
        {
            var requestBytes =
                ServerRequestDataBuilder.ConstructRetryLimitExceededRequest(
                    MyInfo.LocalIpAddress.ToString(),
                    MyInfo.PortNumber,
                    outboundFileTransfer.RemoteServerTransferId,
                    Settings.TransferRetryLimit,
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
                RemoteServerRetryLimit = Settings.TransferRetryLimit,
                RetryLockoutExpireTime = outboundFileTransfer.RetryLockoutExpireTime,
                FileTransferId = outboundFileTransfer.Id
            };

            var sendRequestCompleteEvent = new ServerEvent
            {
                EventType = ServerEventType.SendRetryLimitExceededCompleted,
                RemoteServerIpAddress = outboundFileTransfer.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = outboundFileTransfer.RemoteServerInfo.PortNumber,
                LocalIpAddress = outboundFileTransfer.LocalServerInfo.LocalIpAddress,
                LocalPortNumber = outboundFileTransfer.LocalServerInfo.PortNumber,
                FileTransferId = outboundFileTransfer.Id
            };

            var outboundRequest =
                new ServerRequestController(_requestId, Settings)
                {
                    RemoteServerInfo = RemoteServerInfo
                };

            outboundRequest.EventOccurred += HandleEventOccurred;
            outboundRequest.SocketEventOccurred += HandleSocketEventOccurred;
            outboundRequest.FileTransferId = outboundFileTransfer.Id;

            lock (RequestQueueLock)
            {
                _requests.Add(outboundRequest);
                _requestId++;
            }

            return
                outboundRequest.SendServerRequestAsync(
                    requestBytes,
                    outboundFileTransfer.RemoteServerInfo.SessionIpAddress,
                    outboundFileTransfer.RemoteServerInfo.PortNumber,
                    sendRequestStartedEvent,
                    sendRequestCompleteEvent);
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
                FileTransferId = inboundFileTransfer.Id,
                RequestId = inboundRequest.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            return Result.Ok();
        }

        Task<Result> SendFileTransferResponseAsync(
            ServerRequestType requestType,
            int fileTransferId,
            long responseCode,
            ServerEventType sendRequestStartedEventType,
            ServerEventType sendRequestCompleteEventType)
        {
            var requestBytes =
                ServerRequestDataBuilder.ConstructRequestWithInt64Value(
                    requestType,
                    MyInfo.LocalIpAddress.ToString(),
                    MyInfo.PortNumber,
                    responseCode);

            var sendRequestStartedEvent = new ServerEvent
            {
                EventType = sendRequestStartedEventType,
                RemoteServerIpAddress = RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = RemoteServerInfo.PortNumber,
                LocalIpAddress = MyInfo.LocalIpAddress,
                LocalPortNumber = MyInfo.PortNumber,
                FileTransferId = fileTransferId
            };

            var sendRequestCompleteEvent = new ServerEvent
            {
                EventType = sendRequestCompleteEventType,
                RemoteServerIpAddress = RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = RemoteServerInfo.PortNumber,
                LocalIpAddress = MyInfo.LocalIpAddress,
                LocalPortNumber = MyInfo.PortNumber,
                FileTransferId = fileTransferId
            };

            var outboundRequest =
                new ServerRequestController(_requestId, Settings)
                {
                    RemoteServerInfo = RemoteServerInfo
                };

            outboundRequest.EventOccurred += HandleEventOccurred;
            outboundRequest.SocketEventOccurred += HandleSocketEventOccurred;
            outboundRequest.FileTransferId = fileTransferId;

            lock (RequestQueueLock)
            {
                _requests.Add(outboundRequest);
                _requestId++;
            }

            return
                outboundRequest.SendServerRequestAsync(
                    requestBytes,
                    RemoteServerInfo.SessionIpAddress,
                    RemoteServerInfo.PortNumber,
                    sendRequestStartedEvent,
                    sendRequestCompleteEvent);
        }

        Task<Result> SendBasicServerRequestAsync(
            ServerRequestType requestType,
            ServerEventType sendRequestStartedEventType,
            ServerEventType sendRequestCompleteEventType)
        {
            var requestBytes =
                ServerRequestDataBuilder.ConstructBasicRequest(
                    requestType,
                    MyInfo.LocalIpAddress.ToString(),
                    MyInfo.PortNumber);

            var sendRequestStartedEvent = new ServerEvent
            {
                EventType = sendRequestStartedEventType,
                RemoteServerIpAddress = RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = RemoteServerInfo.PortNumber,
                LocalIpAddress = MyInfo.LocalIpAddress,
                LocalPortNumber = MyInfo.PortNumber
            };

            var sendRequestCompleteEvent = new ServerEvent
            {
                EventType = sendRequestCompleteEventType,
                RemoteServerIpAddress = RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = RemoteServerInfo.PortNumber,
                LocalIpAddress = MyInfo.LocalIpAddress,
                LocalPortNumber = MyInfo.PortNumber
            };

            var outboundRequest =
                new ServerRequestController(_requestId, Settings)
                {
                    RemoteServerInfo = RemoteServerInfo
                };

            outboundRequest.EventOccurred += HandleEventOccurred;
            outboundRequest.SocketEventOccurred += HandleSocketEventOccurred;

            lock (RequestQueueLock)
            {
                _requests.Add(outboundRequest);
                _requestId++;
            }

            return
                outboundRequest.SendServerRequestAsync(
                    requestBytes,
                    RemoteServerInfo.SessionIpAddress,
                    RemoteServerInfo.PortNumber,
                    sendRequestStartedEvent,
                    sendRequestCompleteEvent);
        }

        public async Task<Result> RequestFileListAsync(
            IPAddress remoteServerIpAddress,
            int remoteServerPort,
            string targetFolder)
        {
            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort)
            {
                TransferFolder = targetFolder
            };

            var requestBytes =
                ServerRequestDataBuilder.ConstructRequestWithStringValue(
                    ServerRequestType.FileListRequest,
                    MyInfo.LocalIpAddress.ToString(),
                    MyInfo.PortNumber,
                    RemoteServerInfo.TransferFolder);

            var sendRequestStartedEvent = new ServerEvent
            {
                EventType = ServerEventType.RequestFileListStarted,
                LocalIpAddress = MyInfo.LocalIpAddress,
                LocalPortNumber = MyInfo.PortNumber,
                RemoteServerIpAddress = remoteServerIpAddress,
                RemoteServerPortNumber = remoteServerPort,
                RemoteFolder = targetFolder
            };

            var sendRequestCompleteEvent =
                new ServerEvent {EventType = ServerEventType.RequestFileListComplete};

            var outboundRequest =
                new ServerRequestController(_requestId, Settings)
                {
                    RemoteServerInfo = RemoteServerInfo
                };

            outboundRequest.EventOccurred += HandleEventOccurred;
            outboundRequest.SocketEventOccurred += HandleSocketEventOccurred;

            lock (RequestQueueLock)
            {
                _requests.Add(outboundRequest);
                _requestId++;
            }

            var sendrequest =
                await outboundRequest.SendServerRequestAsync(
                    requestBytes,
                    remoteServerIpAddress,
                    remoteServerPort,
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

        async Task<Result> SendFileListAsync(ServerRequestController inboundRequest)
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
                RemoteServerIpAddress = RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = RemoteServerInfo.PortNumber,
                LocalFolder = MyInfo.TransferFolder,
                RequestId = inboundRequest.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            if (!Directory.Exists(MyInfo.TransferFolder))
            {
                return
                    await SendBasicServerRequestAsync(
                        ServerRequestType.RequestedFolderDoesNotExist,
                        ServerEventType.SendNotificationFolderDoesNotExistStarted,
                        ServerEventType.SendNotificationFolderDoesNotExistComplete).ConfigureAwait(false);
            }

            var fileInfoList = new FileInfoList(MyInfo.TransferFolder);
            if (fileInfoList.Count == 0)
            {
                return
                    await SendBasicServerRequestAsync(
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
                RemoteServerIpAddress = RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = RemoteServerInfo.PortNumber,
                RemoteServerFileList = fileInfoList,
                LocalFolder = MyInfo.TransferFolder
            };

            var sendRequestCompleteEvent =
                new ServerEvent {EventType = ServerEventType.SendFileListComplete};

            var outboundRequest =
                new ServerRequestController(_requestId, Settings)
                {
                    RemoteServerInfo = RemoteServerInfo
                };

            outboundRequest.EventOccurred += HandleEventOccurred;
            outboundRequest.SocketEventOccurred += HandleSocketEventOccurred;

            lock (RequestQueueLock)
            {
                _requests.Add(outboundRequest);
                _requestId++;
            }

            return
                await outboundRequest.SendServerRequestAsync(
                    requestBytes,
                    RemoteServerInfo.SessionIpAddress,
                    RemoteServerInfo.PortNumber,
                    sendRequestStartedEvent,
                    sendRequestCompleteEvent).ConfigureAwait(false);
        }

        void HandleRequestedFolderDoesNotExist(ServerRequestController inboundRequest)
        {
            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceivedNotificationFolderDoesNotExist,
                RemoteServerIpAddress = RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = RemoteServerInfo.PortNumber,
                RequestId = inboundRequest.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());
        }

        void HandleNoFilesAvailableForDownload(ServerRequestController inboundRequest)
        {
            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceivedNotificationNoFilesToDownload,
                RemoteServerIpAddress = RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = RemoteServerInfo.PortNumber,
                RequestId = inboundRequest.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());
        }

        void ReceiveFileList(ServerRequestController inboundRequest)
        {
            var getFileInfoList = inboundRequest.GetRemoteServerFileInfoList();
            RemoteServerFileList = getFileInfoList.Value;

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceivedFileList,
                LocalIpAddress = MyInfo.LocalIpAddress,
                LocalPortNumber = MyInfo.PortNumber,
                RemoteServerIpAddress = RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = RemoteServerInfo.PortNumber,
                RemoteFolder = RemoteServerInfo.TransferFolder,
                RemoteServerFileList = RemoteServerFileList,
                RequestId = inboundRequest.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());
        }

        public async Task<Result> RequestServerInfoAsync(
            IPAddress remoteServerIpAddress,
            int remoteServerPort)
        {
            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);

            var sendrequest =
                await SendBasicServerRequestAsync(
                    ServerRequestType.ServerInfoRequest,
                    ServerEventType.RequestServerInfoStarted,
                    ServerEventType.RequestServerInfoComplete);

            if (sendrequest.Success) return Result.Ok();

            var inboundRequest = _requests.Last();
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

        Task<Result> SendServerInfoAsync(ServerRequestController inboundRequest)
        {
            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceivedServerInfoRequest,
                RemoteServerIpAddress = RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = RemoteServerInfo.PortNumber,
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
                RemoteServerIpAddress = RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = RemoteServerInfo.PortNumber,
                RemoteServerPlatform = MyInfo.Platform,
                LocalFolder = MyInfo.TransferFolder,
                LocalIpAddress = MyInfo.LocalIpAddress,
                LocalPortNumber = MyInfo.PortNumber,
                PublicIpAddress = MyInfo.PublicIpAddress
            };

            var sendRequestCompleteEvent =
                new ServerEvent {EventType = ServerEventType.SendServerInfoComplete};

            var outboundRequest =
                new ServerRequestController(_requestId, Settings)
                {
                    RemoteServerInfo = RemoteServerInfo
                };

            outboundRequest.EventOccurred += HandleEventOccurred;
            outboundRequest.SocketEventOccurred += HandleSocketEventOccurred;

            lock (RequestQueueLock)
            {
                _requests.Add(outboundRequest);
                _requestId++;
            }

            return
                outboundRequest.SendServerRequestAsync(
                    requestBytes,
                    RemoteServerInfo.SessionIpAddress,
                    RemoteServerInfo.PortNumber,
                    sendRequestStartedEvent,
                    sendRequestCompleteEvent);
        }

        void ReceiveServerInfo(ServerRequestController inboundRequest)
        {
            DetermineRemoteServerSessionIpAddress();

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceivedServerInfo,
                RemoteServerIpAddress = RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = RemoteServerInfo.PortNumber,
                RemoteServerPlatform = RemoteServerInfo.Platform,
                RemoteFolder = RemoteServerInfo.TransferFolder,
                LocalIpAddress = RemoteServerInfo.LocalIpAddress,
                PublicIpAddress = RemoteServerInfo.PublicIpAddress,
                RequestId = inboundRequest.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());
        }

        void DetermineRemoteServerSessionIpAddress()
        {
            var checkLocalIp = RemoteServerInfo.LocalIpAddress.IsInRange(_myLanCidrIp);
            if (checkLocalIp.Failure)
            {
                RemoteServerInfo.SessionIpAddress = RemoteServerInfo.PublicIpAddress;
            }

            var remoteServerIsInMyLan = checkLocalIp.Value;

            RemoteServerInfo.SessionIpAddress = remoteServerIsInMyLan
                ? RemoteServerInfo.LocalIpAddress
                : RemoteServerInfo.PublicIpAddress;
        }

        public async Task<Result> ShutdownAsync()
        {
            if (!ServerIsListening)
            {
                return Result.Fail("Server is already shutdown");
            }

            //TODO: This looks awkward, change how shutdown command is sent to local server
            RemoteServerInfo = MyInfo;

            var sendShutdownCommand =
                await SendBasicServerRequestAsync(
                    ServerRequestType.ShutdownServerCommand,
                    ServerEventType.SendShutdownServerCommandStarted,
                    ServerEventType.SendShutdownServerCommandComplete).ConfigureAwait(false);

            return sendShutdownCommand.Success
                ? Result.Ok()
                : Result.Fail($"Error occurred shutting down the server + {sendShutdownCommand.Error}");
        }

        void HandleShutdownServerCommand(ServerRequestController pendingRequest)
        {
            if (MyInfo.IsEqualTo(RemoteServerInfo))
            {
                ShutdownInitiated = true;
            }

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceivedShutdownServerCommand,
                RemoteServerIpAddress = RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = RemoteServerInfo.PortNumber,
                RequestId = pendingRequest.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());
        }

        void ShutdownListenSocket()
        {
            EventOccurred?.Invoke(this,
                new ServerEvent {EventType = ServerEventType.ShutdownListenSocketStarted});

            try
            {
                _listenSocket.Shutdown(SocketShutdown.Both);
                _listenSocket.Close();
            }
            catch (SocketException ex)
            {
                _log.Error("Error raised in method ShutdownListenSocket", ex);
                var errorMessage = $"{ex.Message} ({ex.GetType()} raised in method AsyncFileServer.ShutdownListenSocket)";

                EventOccurred?.Invoke(this,
                    new ServerEvent
                    {
                        EventType = ServerEventType.ShutdownListenSocketCompletedWithError,
                        ErrorMessage = errorMessage
                    });
            }

            EventOccurred?.Invoke(this,
                new ServerEvent {EventType = ServerEventType.ShutdownListenSocketCompletedWithoutError});

            EventOccurred?.Invoke(this,
                new ServerEvent { EventType = ServerEventType.ServerStoppedListening });
        }
    }
}