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

    using Model;
    using Utilities;
    using Common.Extensions;
    using Common.IO;
    using Common.Logging;
    using Common.Network;
    using Common.Result;

    public class AsyncFileServer
    {
        const string NotInitializedMessage =
            "Server is unitialized and cannot handle incoming connections";

        string _myLanCidrIp;
        int _initialized;
        int _busy;
        int _remoteServerAcceptedFileTransfer;
        int _remoteServerRejectedFileTransfer;
        int _transferInProgress;
        int _inboundFileTransferIsStalled;
        int _outboundFileTransferIsStalled;
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
        static readonly object FileLock = new object();
        static readonly object QueueLock = new object();

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

        public AsyncFileServer()
        {
            Platform = Environment.OSVersion.Platform.ToServerPlatform();

            SocketSettings = new SocketSettings
            {
                ListenBacklogSize = 5,
                BufferSize = 1024,
                SocketTimeoutInMilliseconds = 5000
            };

            Info = new ServerInfo()
            {
                TransferFolder = GetDefaultTransferFolder(),
                Name = "AsyncFileServer"
            };

            ServerIsInitialized = false;
            ServerIsListening = false;
            ServerIsBusy = false;
            InboundFileTransferStalled = false;
            OutboundFileTransferStalled = false;
            ShutdownInitiated = false;
            Name = string.Empty;
            RemoteServerInfo = new ServerInfo();
            RemoteServerFileList = new FileInfoList();

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

        public ServerPlatform Platform { get; }
        public float TransferUpdateInterval { get; set; }
        public int TransferRetryLimit { get; set; }
        public TimeSpan RetryLimitLockout { get; set; }
        public SocketSettings SocketSettings { get; set; }
        public ServerInfo Info { get; set; }
        public ServerInfo RemoteServerInfo { get; set; }
        public FileInfoList RemoteServerFileList { get; set; }

        public string Name
        {
            get => Info.Name;
            set => Info.Name = value;
        }

        public string MyTransferFolderPath
        {
            get => Info.TransferFolder;
            set => Info.TransferFolder = value;
        }

        public string RemoteServerTransferFolderPath
        {
            get => RemoteServerInfo.TransferFolder;
            set => RemoteServerInfo.TransferFolder = value;
        }

        public bool IsInitialized => ServerIsInitialized;
        public bool IsListening => ServerIsListening;
        public bool IsBusy => ServerIsBusy;

        public bool NoFileTransfersPending => NoFileTransfersInQueue();
        public int PendingFileTransferCount => PendingTransferCount();
        public Result<ServerRequestController> NextPendingFileTransfer => GetNextFileTransferInQueue();

        public bool NoRequests => _requestId == 1;
        public List<int> RequestIds => _requests.Select(r => r.Id).ToList();
        public int MostRecentRequestId => _requestId - 1;

        public bool NoTextSessions => _textSessions.Count == 0;
        public List<int> TextSessionIds => _textSessions.Select(t => t.Id).ToList();

        public int UnreadTextMessageCount => GetNumberOfUnreadTextMessages();
        public List<int> TextSessionIdsWithUnreadMessages => GetTextSessionIdsWithUnreadMessages();

        public bool NoFileTransfers => _fileTransferId == 1;
        public List<int> FileTransferIds => _fileTransfers.Select(t => t.FiletransferId).ToList();
        public int MostRecentFileTransferId => _fileTransferId - 1;

        public List<int> StalledTransferIds =>
            _fileTransfers.Select(t => t)
                .Where(t => t.TransferStalled)
                .Select(t => t.FiletransferId).ToList();

        public int ListenBacklogSize => SocketSettings.ListenBacklogSize;
        public int BufferSize => SocketSettings.BufferSize;
        public int SocketTimeoutInMilliseconds => SocketSettings.SocketTimeoutInMilliseconds;

        public IPAddress MyLocalIpAddress => Info.LocalIpAddress;
        public IPAddress MyPublicIpAddress => Info.PublicIpAddress;
        public int MyServerPortNumber => Info.PortNumber;

        public IPAddress RemoteServerSessionIpAddress => RemoteServerInfo.SessionIpAddress;
        public IPAddress RemoteServerLocalIpAddress => RemoteServerInfo.LocalIpAddress;
        public IPAddress RemoteServerPublicIpAddress => RemoteServerInfo.PublicIpAddress;
        public int RemoteServerPortNumber => RemoteServerInfo.PortNumber;

        public bool FileTransferAccepted => RemoteServerAcceptedFileTransfer;
        public bool FileTransferRejected => RemoteServerRejectedFileTransfer;
        public bool FileTransferInProgress => TransferInProgress;
        public bool FileTransferCanceled => OutboundFileTransferStalled;
        public bool FileTransferStalled => InboundFileTransferStalled;

        public event EventHandler<ServerEvent> EventOccurred;
        public event EventHandler<ServerEvent> SocketEventOccurred;
        public event EventHandler<ServerEvent> FileTransferProgress;

        public override string ToString()
        {
            return string.IsNullOrEmpty(Name)
                ? "AsyncFileServer"
                : Name;
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

        bool QueueIsEmpty()
        {
            List<ServerRequestController> pendingRequests;

            lock (QueueLock)
            {
                pendingRequests =
                    _requests.Select(r => r).Where(r => r.Status == ServerRequestStatus.Pending).ToList();
            }

            return pendingRequests.Count == 0;
        }

        int PendingTransferCount()
        {
            List<ServerRequestController> pendingTransfers;

            lock (QueueLock)
            {
                pendingTransfers =
                    _requests.Select(r => r)
                        .Where(r => r.IsInboundFileTransferRequest && r.Status == ServerRequestStatus.Pending)
                        .ToList();
            }

            return pendingTransfers.Count;
        }

        Result<ServerRequestController> GetNextFileTransferInQueue()
        {
            var pendingTransfers = _requests.Select(r => r)
                .Where(r => r.IsInboundFileTransferRequest && r.Status == ServerRequestStatus.Pending).ToList();

            return pendingTransfers.Count > 0
                ? Result.Ok(pendingTransfers[0])
                : Result.Fail<ServerRequestController>("Queue contains no pending file transfers");
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

        public Result<ServerRequestController> GetRequestControllerById(int id)
        {
            var matches = _requests.Select(r => r).Where(r => r.Id == id).ToList();

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
            var matches = _fileTransfers.Select(t => t).Where(t => t.FiletransferId == id).ToList();

            if (matches.Count == 0)
            {
                return Result.Fail<FileTransferController>($"No file transfer was found with an ID value of {id}");
            }

            if (matches.Count > 1)
            {
                return Result.Fail<FileTransferController>($"Found {matches.Count} file transfers with the same ID value of {id}");
            }

            var requestedFileTransfer = matches[0];

            requestedFileTransfer.FileTransfer.EventLog =
                _eventLog.Select(e => e).Where(e => e.FileTransferId == requestedFileTransfer.FiletransferId).ToList();

            return Result.Ok(requestedFileTransfer);
        }

        public List<ServerEvent> GetEventLogForFileTransfer(int fileTransferId, LogLevel logLevel)
        {
            var eventLog = new List<ServerEvent>();
            eventLog.AddRange(_eventLog.Select(e => e).Where(e => e.FileTransferId == fileTransferId));

            var matchingRequests =
                _requests.Select(r => r)
                    .Where(r => r.FileTransferId == fileTransferId)
                    .Select(r => r.EventLog);

            foreach (var matchingEvents in matchingRequests)
            {
                eventLog.AddRange(matchingEvents);
            }

            eventLog.RemoveAll(DoNotDisplayInLog);

            if (logLevel == LogLevel.Normal)
            {
                eventLog.RemoveAll(LogLevelIsDebugOnly);
            }

            return eventLog.OrderBy(e => e.TimeStamp).ToList();
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

            eventLog.RemoveAll(DoNotDisplayInLog);

            return eventLog.OrderBy(e => e.TimeStamp).ToList();
        }

        public List<ServerEvent> GetCompleteEventLog(LogLevel logLevel)
        {
            var eventLog = new List<ServerEvent>();
            foreach (var request in _requests)
            {
                eventLog.AddRange(request.EventLog);
            }

            eventLog.AddRange(_eventLog);
            eventLog.RemoveAll(DoNotDisplayInLog);

            if (logLevel == LogLevel.Normal)
            {
                eventLog.RemoveAll(LogLevelIsDebugOnly);
            }

            return eventLog.OrderBy(e => e.TimeStamp).ToList();
        }

        static bool DoNotDisplayInLog(ServerEvent serverEvent)
        {
            return serverEvent.DoNotDisplayInLog;
        }

        static bool LogLevelIsDebugOnly(ServerEvent serverEvent)
        {
            return serverEvent.LogLevelIsDebugOnly;
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

            var requestedFileTransfer = matches[0];

            requestedFileTransfer.FileTransfer.EventLog =
                _eventLog.Select(e => e).Where(e => e.FileTransferId == requestedFileTransfer.FiletransferId).ToList();

            return Result.Ok(requestedFileTransfer);
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

            Info = new ServerInfo
            {
                PortNumber = port,
                LocalIpAddress = localIp,
                PublicIpAddress = publicIp
            };

            if (getLocalIp.Success)
            {
                Info.SessionIpAddress = localIp;
            }
            else if (getPublicIp.Success)
            {
                Info.SessionIpAddress = publicIp;
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

            var listenResult = Listen(MyServerPortNumber);
            if (listenResult.Failure)
            {
                return listenResult;
            }

            ServerIsListening = true;
            var runServerResult = await HandleIncomingRequestsAsync().ConfigureAwait(false);

            ServerIsListening = false;
            ShutdownListenSocket();

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
                return Result.Fail($"{ex.Message} ({ex.GetType()} raised in method AsyncFileServer.Listen)");
            }

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = ServerEventType.ServerStartedListening,
                    LocalPortNumber = MyServerPortNumber
                });

            return Result.Ok();
        }

        async Task<Result> HandleIncomingRequestsAsync()
        {
            // Main loop. Server handles incoming connections until shutdown command is received
            // or an error is encountered
            while (true)
            {
                if (!NoFileTransfersPending)
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
                    new ServerRequestController(_requestId, BufferSize, SocketTimeoutInMilliseconds);

                inboundRequest.EventOccurred += HandleEventOccurred;
                inboundRequest.SocketEventOccurred += HandleSocketEventOccurred;

                var receiveRequest = await inboundRequest.ReceiveServerRequestAsync(socket).ConfigureAwait(false);
                if (receiveRequest.Failure)
                {
                    return receiveRequest;
                }

                lock (QueueLock)
                {
                    _requests.Add(inboundRequest);
                    _requestId++;
                }

                if (_token.IsCancellationRequested || ShutdownInitiated) return Result.Ok();
                if (inboundRequest.IsInboundFileTransferRequest) continue;
                if (ServerIsBusy) continue;

                var processRequest = await ProcessRequestAsync(inboundRequest).ConfigureAwait(false);
                if (processRequest.Failure)
                {
                    return processRequest;
                }
            }
        }

        public async Task<Result> ProcessNextRequestInQueueAsync()
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

            return await ProcessRequestAsync(getPendingTransfer.Value).ConfigureAwait(false);
        }

        async Task<Result> ProcessRequestAsync(ServerRequestController inboundRequest)
        {
            ServerIsBusy = true;
            RemoteServerInfo = inboundRequest.RemoteServerInfo;
            RemoteServerTransferFolderPath = inboundRequest.RemoteServerInfo.TransferFolder;

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ProcessRequestStarted,
                RequestType = inboundRequest.RequestType,
                RequestId = inboundRequest.Id,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            var result = Result.Ok();

            switch (inboundRequest.RequestType)
            {
                case ServerRequestType.TextMessage:
                    result = ReceiveTextMessage(inboundRequest);
                    break;

                case ServerRequestType.InboundFileTransferRequest:
                    result = await HandleInboundFileTransferRequestAsync(inboundRequest, _token).ConfigureAwait(false);
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
                    var error = $"Unable to determine transfer type, value of '{inboundRequest.RequestType}' is invalid.";
                    return Result.Fail(error);
            }

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ProcessRequestComplete,
                RequestType = inboundRequest.RequestType,
                RequestId = inboundRequest.Id,
                RemoteServerIpAddress = RemoteServerSessionIpAddress
            });

            EventOccurred?.Invoke(this, _eventLog.Last());
            ServerIsBusy = false;

            //var processRequestEvents =
            //    _eventLog.Select(e => e).Where(e => e.RequestId == inboundRequest.Id).ToList();

            //inboundRequest.EventLog.AddRange(processRequestEvents);
            inboundRequest.Status = ServerRequestStatus.Processed;
            inboundRequest.ShutdownSocket();

            if (result.Success) return await HandleRemainingRequestsInQueueAsync().ConfigureAwait(false);

            EventOccurred?.Invoke(this, new ServerEvent
            {
                EventType = ServerEventType.ErrorOccurred,
                ErrorMessage = result.Error
            });

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

        async Task<Result> HandleRemainingRequestsInQueueAsync()
        {
            if (QueueIsEmpty()) return Result.Ok();

            foreach (var request in _requests.Select(r => r).Where(r => r.Status == ServerRequestStatus.Pending))
            {
                if (request.IsInboundFileTransferRequest) continue;

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
                    MyLocalIpAddress.ToString(),
                    MyServerPortNumber,
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
                new ServerRequestController(_requestId, BufferSize, SocketTimeoutInMilliseconds)
                {
                    RemoteServerInfo = RemoteServerInfo
                };

            outboundRequest.EventOccurred += HandleEventOccurred;
            outboundRequest.SocketEventOccurred += HandleSocketEventOccurred;

            lock (QueueLock)
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

        public async Task<Result> SendFileAsync(IPAddress remoteServerIpAddress,
            int remoteServerPort,
            string remoteServerName,
            string localFilePath,
            string remoteFolderPath)
        {
            if (!File.Exists(localFilePath))
            {
                return Result.Fail("File does not exist: " + localFilePath);
            }

            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);

            var outboundFileTransfer = new FileTransfer(BufferSize)
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
                RemoteServerName = remoteServerName,
                LocalFilePath = localFilePath,
                LocalFolderPath = Path.GetDirectoryName(localFilePath),
                RemoteFolderPath = remoteFolderPath,
                RemoteFilePath = Path.Combine(remoteFolderPath, Path.GetFileName(localFilePath)),
                FileSizeInBytes = new FileInfo(localFilePath).Length,
                RequestInitiatedTime = DateTime.Now
            };

            var fileTransferController = new FileTransferController(outboundFileTransfer);
            _fileTransfers.Add(fileTransferController);
            _fileTransferId++;

            return await SendOutboundFileTransferRequestAsync(outboundFileTransfer).ConfigureAwait(false);
        }

        async Task<Result> HandleOutboundFileTransferRequestAsync(ServerRequestController inboundRequest)
        {
            var getFileTransfer = inboundRequest.GetOutboundFileTransfer(Info, _fileTransferId);
            if (getFileTransfer.Failure)
            {
                return getFileTransfer;
            }

            var outboundFileTransfer = getFileTransfer.Value;
            _fileTransferId++;

            if (!File.Exists(outboundFileTransfer.LocalFilePath))
            {
                return await SendFileTransferResponseAsync(
                    ServerRequestType.RequestedFileDoesNotExist,
                    outboundFileTransfer.RemoteServerTransferId,
                    outboundFileTransfer.RemoteServerTransferId,
                    ServerEventType.SendNotificationFileDoesNotExistStarted,
                    ServerEventType.SendNotificationFileDoesNotExistComplete).ConfigureAwait(false);
            }

            // TODO: Create logic to check stalled file transfers that are under lockout and if this request matches remoteserver info + localfilepath, send a new filetranserresponse = rejected_retrylimitexceeded. maybe we should penalize them for trying to subvert our lockout policy?

            var fileTransferController = new FileTransferController(outboundFileTransfer);
            _fileTransfers.Add(fileTransferController);

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceivedOutboundFileTransferRequest,
                LocalFolder = Path.GetDirectoryName(outboundFileTransfer.LocalFilePath),
                FileName = Path.GetFileName(outboundFileTransfer.LocalFilePath),
                FileSizeInBytes = new FileInfo(outboundFileTransfer.LocalFilePath).Length,
                RemoteFolder = outboundFileTransfer.RemoteFolderPath,
                RemoteServerIpAddress = outboundFileTransfer.RemoteServerIpAddress,
                RemoteServerPortNumber = outboundFileTransfer.RemoteServerPortNumber,
                RequestId = inboundRequest.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            return await SendOutboundFileTransferRequestAsync(outboundFileTransfer).ConfigureAwait(false);
        }

        async Task<Result> SendOutboundFileTransferRequestAsync(FileTransfer outboundFileTransfer)
        {
            RemoteServerAcceptedFileTransfer = false;
            RemoteServerRejectedFileTransfer = false;

            var requestBytes =
                ServerRequestDataBuilder.ConstructOutboundFileTransferRequest(
                    MyLocalIpAddress.ToString(),
                    MyServerPortNumber,
                    outboundFileTransfer);

            var sendRequestStartedEvent = new ServerEvent
            {
                EventType = ServerEventType.RequestOutboundFileTransferStarted,
                LocalIpAddress = MyLocalIpAddress,
                LocalPortNumber = MyServerPortNumber,
                LocalFolder = outboundFileTransfer.LocalFolderPath,
                FileName = Path.GetFileName(outboundFileTransfer.LocalFilePath),
                FileSizeInBytes = outboundFileTransfer.FileSizeInBytes,
                RemoteServerIpAddress = outboundFileTransfer.RemoteServerIpAddress,
                RemoteServerPortNumber = outboundFileTransfer.RemoteServerPortNumber,
                RemoteFolder = outboundFileTransfer.RemoteFolderPath,
                FileTransferId = outboundFileTransfer.Id
            };

            var sendRequestCompleteEvent = new ServerEvent
            {
                EventType = ServerEventType.RequestOutboundFileTransferComplete,
                LocalIpAddress = MyLocalIpAddress,
                LocalPortNumber = MyServerPortNumber,
                LocalFolder = outboundFileTransfer.LocalFolderPath,
                FileName = Path.GetFileName(outboundFileTransfer.LocalFilePath),
                FileSizeInBytes = outboundFileTransfer.FileSizeInBytes,
                RemoteServerIpAddress = outboundFileTransfer.RemoteServerIpAddress,
                RemoteServerPortNumber = outboundFileTransfer.RemoteServerPortNumber,
                RemoteFolder = outboundFileTransfer.RemoteFolderPath,
                FileTransferId = outboundFileTransfer.Id
            };

            var outboundRequest =
                new ServerRequestController(_requestId, BufferSize, SocketTimeoutInMilliseconds)
                {
                    RemoteServerInfo = RemoteServerInfo
                };

            outboundRequest.EventOccurred += HandleEventOccurred;
            outboundRequest.SocketEventOccurred += HandleSocketEventOccurred;
            outboundRequest.FileTransferId = outboundFileTransfer.Id;

            lock (QueueLock)
            {
                _requests.Add(outboundRequest);
                _requestId++;
            }

            var sendRequest =
                await outboundRequest.SendServerRequestAsync(
                    requestBytes,
                    outboundFileTransfer.RemoteServerIpAddress,
                    outboundFileTransfer.RemoteServerPortNumber,
                    sendRequestStartedEvent,
                    sendRequestCompleteEvent).ConfigureAwait(false);

            if (sendRequest.Success) return Result.Ok();

            outboundFileTransfer.Status = FileTransferStatus.Error;
            outboundFileTransfer.ErrorMessage = sendRequest.Error;

            return sendRequest;
        }

        Result HandleFileTransferRejected(ServerRequestController inboundRequest)
        {
            RemoteServerRejectedFileTransfer = true;

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

            var outboundFileTransfer = getFileTransfer.Value.FileTransfer;
            outboundFileTransfer.Status = FileTransferStatus.Rejected;
            outboundFileTransfer.TransferStartTime = DateTime.Now;
            outboundFileTransfer.TransferCompleteTime = outboundFileTransfer.TransferStartTime;

            inboundRequest.FileTransferId = outboundFileTransfer.Id;

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.RemoteServerRejectedFileTransfer,
                RemoteServerIpAddress = outboundFileTransfer.RemoteServerIpAddress,
                RemoteServerPortNumber = outboundFileTransfer.RemoteServerPortNumber,
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
            RemoteServerAcceptedFileTransfer = true;
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

            var outboundFileTransfer = getFileTransfer.Value.FileTransfer;

            var getSendSocket = inboundRequest.GetTransferSocket();
            if (getSendSocket.Failure)
            {
                return getSendSocket;
            }

            outboundFileTransfer.SendSocket = getSendSocket.Value;
            outboundFileTransfer.Status = FileTransferStatus.Accepted;

            inboundRequest.FileTransferId = outboundFileTransfer.Id;

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.RemoteServerAcceptedFileTransfer,
                RemoteServerIpAddress = outboundFileTransfer.RemoteServerIpAddress,
                RemoteServerPortNumber = outboundFileTransfer.RemoteServerPortNumber,
                FileTransferId = outboundFileTransfer.Id,
                RequestId = inboundRequest.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            var sendFileBytes =
                await SendFileBytesAsync(
                    outboundFileTransfer,
                    inboundRequest,
                    token)
                    .ConfigureAwait(false);

            if (sendFileBytes.Success) return Result.Ok();

            outboundFileTransfer.Status = FileTransferStatus.Error;
            outboundFileTransfer.ErrorMessage = sendFileBytes.Error;
            outboundFileTransfer.TransferCompleteTime = DateTime.Now;

            TransferInProgress = false;

            return sendFileBytes;
        }

        async Task<Result> SendFileBytesAsync(
            FileTransfer fileTransfer,
            ServerRequestController inboundRequest,
            CancellationToken token)
        {
            fileTransfer.Status = FileTransferStatus.InProgress;
            fileTransfer.TransferStartTime = DateTime.Now;

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.SendFileBytesStarted,
                RemoteServerIpAddress = fileTransfer.RemoteServerIpAddress,
                RemoteServerPortNumber = fileTransfer.RemoteServerPortNumber,
                FileTransferId = fileTransfer.Id,
                RequestId = inboundRequest.Id
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
                    fileTransfer.Buffer = new byte[fileChunkSize];

                    var numberOfBytesToSend = file.Read(fileTransfer.Buffer, 0, fileChunkSize);
                    fileTransfer.BytesRemaining -= numberOfBytesToSend;

                    var offset = 0;
                    var socketSendCount = 0;
                    while (numberOfBytesToSend > 0)
                    {
                        var sendFileChunkResult =
                            await fileTransfer.SendSocket.SendWithTimeoutAsync(
                                fileTransfer.Buffer,
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

                    _eventLog.Add(new ServerEvent
                    {
                        EventType = ServerEventType.SentFileChunkToClient,
                        FileSizeInBytes = fileTransfer.FileSizeInBytes,
                        CurrentFileBytesSent = fileChunkSize,
                        FileBytesRemaining = fileTransfer.BytesRemaining,
                        FileChunkSentCount = fileTransfer.FileChunkSentCount,
                        SocketSendCount = socketSendCount,
                        FileTransferId = fileTransfer.Id,
                        RequestId = inboundRequest.Id
                    });

                    SocketEventOccurred?.Invoke(this, _eventLog.Last());
                }

                fileTransfer.Status = FileTransferStatus.AwaitingConfirmation;
                fileTransfer.TransferCompleteTime = DateTime.Now;
                fileTransfer.PercentComplete = 1;
                fileTransfer.CurrentBytesSent = 0;
                fileTransfer.BytesRemaining = 0;

                _eventLog.Add(new ServerEvent
                {
                    EventType = ServerEventType.SendFileBytesComplete,
                    RemoteServerIpAddress = fileTransfer.RemoteServerIpAddress,
                    RemoteServerPortNumber = fileTransfer.RemoteServerPortNumber,
                    FileTransferId = fileTransfer.Id,
                    RequestId = inboundRequest.Id
                });

                EventOccurred?.Invoke(this, _eventLog.Last());
                TransferInProgress = false;

                return Result.Ok();
            }
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

            var outboundFileTransfer = getFileTransfer.Value.FileTransfer;
            outboundFileTransfer.Status = FileTransferStatus.Complete;

            inboundRequest.FileTransferId = outboundFileTransfer.Id;

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.RemoteServerConfirmedFileTransferCompleted,
                RemoteServerIpAddress = outboundFileTransfer.RemoteServerIpAddress,
                RemoteServerPortNumber = outboundFileTransfer.RemoteServerPortNumber,
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
            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);
            RemoteServerTransferFolderPath = Path.GetDirectoryName(remoteFilePath);
            MyTransferFolderPath = localFolderPath;

            var inboundFileTransfer = new FileTransfer(BufferSize)
            {
                Id = _fileTransferId,
                TransferDirection = FileTransferDirection.Inbound,
                Initiator = FileTransferInitiator.Self,
                Status = FileTransferStatus.AwaitingResponse,
                MyLocalIpAddress = MyLocalIpAddress,
                MyPublicIpAddress = MyPublicIpAddress,
                MyServerPortNumber = MyServerPortNumber,
                RemoteServerIpAddress = remoteServerIpAddress,
                RemoteServerPortNumber = remoteServerPort,
                RemoteServerName = remoteServerName,
                LocalFilePath = Path.Combine(localFolderPath, Path.GetFileName(remoteFilePath)),
                LocalFolderPath = localFolderPath,
                FileSizeInBytes = fileSizeBytes,
                RemoteFolderPath = Path.GetDirectoryName(remoteFilePath),
                RemoteFilePath = remoteFilePath,
                RequestInitiatedTime = DateTime.Now
            };

            var fileTransferController = new FileTransferController(inboundFileTransfer);
            _fileTransfers.Add(fileTransferController);
            _fileTransferId++;

            var requestBytes =
                ServerRequestDataBuilder.ConstructInboundFileTransferRequest(
                    MyLocalIpAddress.ToString(),
                    MyServerPortNumber,
                    inboundFileTransfer);

            var sendRequestStartedEvent = new ServerEvent
            {
                EventType = ServerEventType.RequestInboundFileTransferStarted,
                RemoteServerIpAddress = inboundFileTransfer.RemoteServerIpAddress,
                RemoteServerPortNumber = inboundFileTransfer.RemoteServerPortNumber,
                RemoteFolder = inboundFileTransfer.RemoteFolderPath,
                FileName = Path.GetFileName(remoteFilePath),
                FileSizeInBytes = fileSizeBytes,
                LocalIpAddress = MyLocalIpAddress,
                LocalPortNumber = MyServerPortNumber,
                LocalFolder = MyTransferFolderPath,
                FileTransferId = inboundFileTransfer.Id
            };

            var sendRequestCompleteEvent = new ServerEvent
            {
                EventType = ServerEventType.RequestInboundFileTransferComplete,
                FileTransferId = inboundFileTransfer.Id
            };

            var outboundRequest =
                new ServerRequestController(BufferSize, SocketTimeoutInMilliseconds, _requestId)
                {
                    RemoteServerInfo = RemoteServerInfo
                };

            outboundRequest.EventOccurred += HandleEventOccurred;
            outboundRequest.SocketEventOccurred += HandleSocketEventOccurred;
            outboundRequest.FileTransferId = inboundFileTransfer.Id;

            lock (QueueLock)
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

            inboundFileTransfer.Status = FileTransferStatus.Error;
            inboundFileTransfer.ErrorMessage = sendRequest.Error;

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

            var inboundFileTransfer = getFileTransfer.Value.FileTransfer;
            inboundFileTransfer.Status = FileTransferStatus.Rejected;
            inboundFileTransfer.TransferStartTime = DateTime.Now;
            inboundFileTransfer.TransferCompleteTime = inboundFileTransfer.TransferStartTime;

            inboundRequest.FileTransferId = inboundFileTransfer.Id;

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceivedNotificationFileDoesNotExist,
                RemoteServerIpAddress = inboundFileTransfer.RemoteServerIpAddress,
                RemoteServerPortNumber = inboundFileTransfer.RemoteServerPortNumber,
                FileTransferId = inboundFileTransfer.Id,
                RequestId = inboundRequest.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());
            TransferInProgress = false;

            return Result.Ok();
        }

        async Task<Result> HandleInboundFileTransferRequestAsync(
            ServerRequestController inboundRequest,
            CancellationToken token)
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
                FileName = Path.GetFileName(inboundFileTransfer.LocalFilePath),
                FileSizeInBytes = inboundFileTransfer.FileSizeInBytes,
                RemoteServerIpAddress = inboundFileTransfer.RemoteServerIpAddress,
                RemoteServerPortNumber = inboundFileTransfer.RemoteServerPortNumber,
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

            var acceptFileTransfer =
                await AcceptFileTransferAsync(
                    inboundRequest,
                    inboundFileTransfer.Id,
                    inboundFileTransfer.TransferResponseCode,
                    ServerEventType.SendFileTransferAcceptedStarted,
                    ServerEventType.SendFileTransferAcceptedComplete).ConfigureAwait(false);

            if (acceptFileTransfer.Failure)
            {
                inboundFileTransfer.Status = FileTransferStatus.Error;
                inboundFileTransfer.ErrorMessage = acceptFileTransfer.Error;

                return acceptFileTransfer;
            }

            var getReceiveSocket = inboundRequest.GetTransferSocket();
            if (getReceiveSocket.Failure)
            {
                return getReceiveSocket;
            }

            inboundFileTransfer.ReceiveSocket = getReceiveSocket.Value;
            inboundFileTransfer.Status = FileTransferStatus.Accepted;

            var receiveFile =
                await ReceiveFileAsync(
                    inboundFileTransfer,
                    inboundRequest,
                    token)
                    .ConfigureAwait(false);

            if (receiveFile.Success) return Result.Ok();

            inboundFileTransfer.Status = FileTransferStatus.Error;
            inboundFileTransfer.ErrorMessage = receiveFile.Error;

            return receiveFile;
        }

        Result<FileTransfer> GetInboundFileTransfer(ServerRequestController inboundRequest)
        {
            FileTransfer inboundFileTransfer;
            var getFileTransferId = inboundRequest.GetInboundFileTransferId();
            if (getFileTransferId.Success)
            {
                var fileTransferId = getFileTransferId.Value;
                var getFileTransfer = GetFileTransferById(fileTransferId);
                if (getFileTransfer.Failure)
                {
                    return Result.Fail<FileTransfer>(getFileTransfer.Error);
                }

                inboundFileTransfer = getFileTransfer.Value.FileTransfer;

                var syncFileTransfer = inboundRequest.UpdateInboundFileTransfer(inboundFileTransfer);
                if (syncFileTransfer.Failure)
                {
                    return Result.Fail<FileTransfer>(syncFileTransfer.Error);
                }

                inboundFileTransfer = syncFileTransfer.Value;
            }
            else
            {
                var getFileTransfer = inboundRequest.GetInboundFileTransfer(Info, _fileTransferId);
                if (getFileTransfer.Failure)
                {
                    return getFileTransfer;
                }

                inboundFileTransfer = getFileTransfer.Value;
                _fileTransferId++;

                var fileTransferController = new FileTransferController(inboundFileTransfer);
                _fileTransfers.Add(fileTransferController);
            }

            return Result.Ok(inboundFileTransfer);
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
                    MyLocalIpAddress.ToString(),
                    MyServerPortNumber,
                    responseCode);

            var sendRequestStartedEvent = new ServerEvent
            {
                EventType = sendRequestStartedEventType,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                LocalIpAddress = MyLocalIpAddress,
                LocalPortNumber = MyServerPortNumber,
                FileTransferId = fileTransferId
            };

            var sendRequestCompleteEvent = new ServerEvent
            {
                EventType = sendRequestCompleteEventType,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                LocalIpAddress = MyLocalIpAddress,
                LocalPortNumber = MyServerPortNumber,
                FileTransferId = fileTransferId
            };

            return
                inboundRequest.SendServerRequestAsync(
                    requestBytes,
                    RemoteServerSessionIpAddress,
                    RemoteServerPortNumber,
                    sendRequestStartedEvent,
                    sendRequestCompleteEvent);
        }

        async Task<Result> ReceiveFileAsync(
            FileTransfer fileTransfer,
            ServerRequestController inboundRequest,
            CancellationToken token)
        {
            fileTransfer.Status = FileTransferStatus.InProgress;
            fileTransfer.TransferStartTime = DateTime.Now;

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceiveFileBytesStarted,
                RemoteServerIpAddress = fileTransfer.RemoteServerIpAddress,
                RemoteServerPortNumber = fileTransfer.RemoteServerPortNumber,
                FileTransferStartTime = fileTransfer.TransferStartTime,
                FileSizeInBytes = fileTransfer.FileSizeInBytes,
                FileTransferId = fileTransfer.Id,
                RequestId = inboundRequest.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            var receiveCount = 0;
            fileTransfer.TotalBytesReceived = 0;
            fileTransfer.BytesRemaining = fileTransfer.FileSizeInBytes;
            fileTransfer.PercentComplete = 0;
            InboundFileTransferStalled = false;

            var unreadBytes = inboundRequest.UnreadBytes.ToArray();
            if (unreadBytes.Length > 0)
            {
                fileTransfer.TotalBytesReceived += unreadBytes.Length;
                fileTransfer.BytesRemaining -= unreadBytes.Length;

                lock (FileLock)
                {
                    var writeBytesToFile =
                        FileHelper.WriteBytesToFile(
                            fileTransfer.LocalFilePath,
                            unreadBytes,
                            unreadBytes.Length,
                            10);

                    if (writeBytesToFile.Failure)
                    {
                        return writeBytesToFile;
                    }
                }

                _eventLog.Add(new ServerEvent
                {
                    EventType = ServerEventType.CopySavedBytesToIncomingFile,
                    CurrentFileBytesReceived = unreadBytes.Length,
                    TotalFileBytesReceived = fileTransfer.TotalBytesReceived,
                    FileSizeInBytes = fileTransfer.FileSizeInBytes,
                    FileBytesRemaining = fileTransfer.BytesRemaining,
                    FileTransferId = fileTransfer.Id,
                    RequestId = inboundRequest.Id
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

                var readFromSocket =
                    await fileTransfer.ReceiveSocket.ReceiveWithTimeoutAsync(
                            fileTransfer.Buffer,
                            0,
                            BufferSize,
                            SocketFlags.None,
                            SocketTimeoutInMilliseconds)
                        .ConfigureAwait(false);

                if (readFromSocket.Failure)
                {
                    return readFromSocket;
                }

                fileTransfer.CurrentBytesReceived = readFromSocket.Value;
                var receivedBytes = new byte[fileTransfer.CurrentBytesReceived];

                if (fileTransfer.CurrentBytesReceived == 0)
                {
                    return Result.Fail("Socket is no longer receiving data, must abort file transfer");
                }

                int fileWriteAttempts;
                lock (FileLock)
                {
                    var writeBytesToFile = FileHelper.WriteBytesToFile(
                        fileTransfer.LocalFilePath,
                        receivedBytes,
                        fileTransfer.CurrentBytesReceived,
                        100);

                    if (writeBytesToFile.Failure)
                    {
                        return writeBytesToFile;
                    }

                    fileWriteAttempts = writeBytesToFile.Value + 1;
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
                        EventType = ServerEventType.MultipleFileWriteAttemptsNeeded,
                        FileWriteAttempts = fileWriteAttempts,
                        PercentComplete = fileTransfer.PercentComplete,
                        FileTransferId = fileTransfer.Id,
                        RequestId = inboundRequest.Id
                    });

                    EventOccurred?.Invoke(this, _eventLog.Last());
                }

                // this method fires on every socket read event, which could be hurdreds of thousands
                // of times depending on the file size and buffer size. Since this  event is only used
                // by myself when debugging small test files, I limited this event to only fire when 
                // the size of the file will result in less than 10 read events
                if (fileTransfer.FileSizeInBytes < 10 * BufferSize)
                {
                    _eventLog.Add(new ServerEvent
                    {
                        EventType = ServerEventType.ReceivedFileBytesFromSocket,
                        SocketReadCount = receiveCount,
                        BytesReceivedCount = fileTransfer.CurrentBytesReceived,
                        CurrentFileBytesReceived = fileTransfer.CurrentBytesReceived,
                        TotalFileBytesReceived = fileTransfer.TotalBytesReceived,
                        FileSizeInBytes = fileTransfer.FileSizeInBytes,
                        FileBytesRemaining = fileTransfer.BytesRemaining,
                        PercentComplete = fileTransfer.PercentComplete,
                        FileTransferId = fileTransfer.Id,
                        RequestId = inboundRequest.Id
                    });

                    SocketEventOccurred?.Invoke(this, _eventLog.Last());
                }

                // Report progress in intervals which are set by the user in the settings file
                if (changeSinceLastUpdate < TransferUpdateInterval) continue;

                fileTransfer.PercentComplete = checkPercentComplete;

                FileTransferProgress?.Invoke(this, new ServerEvent
                {
                    EventType = ServerEventType.UpdateFileTransferProgress,
                    TotalFileBytesReceived = fileTransfer.TotalBytesReceived,
                    PercentComplete = fileTransfer.PercentComplete
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
                EventType = ServerEventType.ReceiveFileBytesComplete,
                FileTransferStartTime = fileTransfer.TransferStartTime,
                FileTransferCompleteTime = DateTime.Now,
                FileSizeInBytes = fileTransfer.FileSizeInBytes,
                RemoteServerIpAddress = fileTransfer.RemoteServerIpAddress,
                RemoteServerPortNumber = fileTransfer.RemoteServerPortNumber,
                FileTransferId = fileTransfer.Id,
                RequestId = inboundRequest.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            var confirmFileTransfer =
                await SendFileTransferResponseAsync(
                    ServerRequestType.FileTransferComplete,
                    fileTransfer.Id,
                    fileTransfer.TransferResponseCode,
                    ServerEventType.SendFileTransferCompletedStarted,
                    ServerEventType.SendFileTransferCompletedCompleted).ConfigureAwait(false);

            if (confirmFileTransfer.Success) return Result.Ok();

            fileTransfer.Status = FileTransferStatus.Error;
            fileTransfer.ErrorMessage = confirmFileTransfer.Error;

            return confirmFileTransfer;
        }

        public async Task<Result> SendNotificationFileTransferStalledAsync(int fileTransferId)
        {
            const string fileTransferStalledErrorMessage =
                "Data is no longer bring received from remote client, file transfer has been canceled (SendNotificationFileTransferStalledAsync)";

            var getFileTransfer = GetFileTransferById(fileTransferId);
            if (getFileTransfer.Failure)
            {
                var error = $"{getFileTransfer.Error} (AsyncFileServer.SendNotificationFileTransferStalledAsync)";
                return Result.Fail(error);
            }

            var inboundFileTransfer = getFileTransfer.Value.FileTransfer;
            inboundFileTransfer.Status = FileTransferStatus.Stalled;
            inboundFileTransfer.TransferCompleteTime = DateTime.Now;
            inboundFileTransfer.ErrorMessage = fileTransferStalledErrorMessage;

            InboundFileTransferStalled = true;

            return await
                SendFileTransferResponseAsync(
                    ServerRequestType.FileTransferStalled,
                    inboundFileTransfer.Id,
                    inboundFileTransfer.TransferResponseCode,
                    ServerEventType.SendFileTransferStalledStarted,
                    ServerEventType.SendFileTransferStalledComplete).ConfigureAwait(false);
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

            var outboundFileTransfer = getFileTransfer.Value.FileTransfer;
            outboundFileTransfer.Status = FileTransferStatus.Cancelled;
            outboundFileTransfer.TransferCompleteTime = DateTime.Now;
            outboundFileTransfer.ErrorMessage = fileTransferStalledErrorMessage;

            inboundRequest.FileTransferId = outboundFileTransfer.Id;

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.FileTransferStalled,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                FileTransferId = outboundFileTransfer.Id,
                RequestId = inboundRequest.Id
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
                var error = $"{getFileTransferResult.Error} (AsyncFileServer.RetryFileTransferAsync)";
                return Result.Fail(error);
            }

            var stalledFileTransfer = getFileTransferResult.Value;

            return await
                SendFileTransferResponseAsync(
                    ServerRequestType.RetryOutboundFileTransfer,
                    stalledFileTransfer.FiletransferId,
                    stalledFileTransfer.TransferResponseCode,
                    ServerEventType.RetryOutboundFileTransferStarted,
                    ServerEventType.RetryOutboundFileTransferComplete).ConfigureAwait(false);
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

            var canceledFileTransfer = getFileTransfer.Value.FileTransfer;

            inboundRequest.FileTransferId = canceledFileTransfer.Id;

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceivedRetryOutboundFileTransferRequest,
                LocalFolder = Path.GetDirectoryName(canceledFileTransfer.LocalFilePath),
                FileName = Path.GetFileName(canceledFileTransfer.LocalFilePath),
                FileSizeInBytes = new FileInfo(canceledFileTransfer.LocalFilePath).Length,
                RemoteFolder = canceledFileTransfer.RemoteFolderPath,
                RemoteServerIpAddress = canceledFileTransfer.RemoteServerIpAddress,
                RemoteServerPortNumber = canceledFileTransfer.RemoteServerPortNumber,
                RetryCounter = canceledFileTransfer.RetryCounter,
                RemoteServerRetryLimit = TransferRetryLimit,
                FileTransferId = canceledFileTransfer.Id,
                RequestId = inboundRequest.Id
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

                return await SendRetryLimitExceededAsync(canceledFileTransfer).ConfigureAwait(false);
            }

            canceledFileTransfer.ResetTransferValues();
            canceledFileTransfer.RemoteServerRetryLimit = TransferRetryLimit;

            return await SendOutboundFileTransferRequestAsync(canceledFileTransfer).ConfigureAwait(false);
        }

        Task<Result> SendRetryLimitExceededAsync(FileTransfer outboundFileTransfer)
        {
            var requestBytes =
                ServerRequestDataBuilder.ConstructRetryLimitExceededRequest(
                    MyLocalIpAddress.ToString(),
                    MyServerPortNumber,
                    outboundFileTransfer.RemoteServerTransferId,
                    TransferRetryLimit,
                    outboundFileTransfer.RetryLockoutExpireTime.Ticks);

            var sendRequestStartedEvent = new ServerEvent
            {
                EventType = ServerEventType.SendRetryLimitExceededStarted,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                LocalIpAddress = MyLocalIpAddress,
                LocalPortNumber = MyServerPortNumber,
                FileName = Path.GetFileName(outboundFileTransfer.LocalFilePath),
                RetryCounter = outboundFileTransfer.RetryCounter,
                RemoteServerRetryLimit = TransferRetryLimit,
                RetryLockoutExpireTime = outboundFileTransfer.RetryLockoutExpireTime,
                FileTransferId = outboundFileTransfer.Id
            };

            var sendRequestCompleteEvent = new ServerEvent
            {
                EventType = ServerEventType.SendRetryLimitExceededCompleted,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                LocalIpAddress = MyLocalIpAddress,
                LocalPortNumber = MyServerPortNumber,
                FileTransferId = outboundFileTransfer.Id
            };

            var outboundRequest =
                new ServerRequestController(_requestId, BufferSize, SocketTimeoutInMilliseconds)
                {
                    RemoteServerInfo = RemoteServerInfo
                };

            outboundRequest.EventOccurred += HandleEventOccurred;
            outboundRequest.SocketEventOccurred += HandleSocketEventOccurred;
            outboundRequest.FileTransferId = outboundFileTransfer.Id;

            lock (QueueLock)
            {
                _requests.Add(outboundRequest);
                _requestId++;
            }

            return
                outboundRequest.SendServerRequestAsync(
                    requestBytes,
                    outboundFileTransfer.RemoteServerIpAddress,
                    outboundFileTransfer.RemoteServerPortNumber,
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

            var inboundFileTransfer = getFileTransfer.Value.FileTransfer;
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
                FileName = Path.GetFileName(inboundFileTransfer.LocalFilePath),
                FileSizeInBytes = inboundFileTransfer.FileSizeInBytes,
                RemoteServerIpAddress = inboundFileTransfer.RemoteServerIpAddress,
                RemoteServerPortNumber = inboundFileTransfer.RemoteServerPortNumber,
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
                    MyLocalIpAddress.ToString(),
                    MyServerPortNumber,
                    responseCode);

            var sendRequestStartedEvent = new ServerEvent
            {
                EventType = sendRequestStartedEventType,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                LocalIpAddress = MyLocalIpAddress,
                LocalPortNumber = MyServerPortNumber,
                FileTransferId = fileTransferId
            };

            var sendRequestCompleteEvent = new ServerEvent
            {
                EventType = sendRequestCompleteEventType,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                LocalIpAddress = MyLocalIpAddress,
                LocalPortNumber = MyServerPortNumber,
                FileTransferId = fileTransferId
            };

            var outboundRequest =
                new ServerRequestController(_requestId, BufferSize, SocketTimeoutInMilliseconds)
                {
                    RemoteServerInfo = RemoteServerInfo
                };

            outboundRequest.EventOccurred += HandleEventOccurred;
            outboundRequest.SocketEventOccurred += HandleSocketEventOccurred;
            outboundRequest.FileTransferId = fileTransferId;

            lock (QueueLock)
            {
                _requests.Add(outboundRequest);
                _requestId++;
            }

            return
                outboundRequest.SendServerRequestAsync(
                    requestBytes,
                    RemoteServerSessionIpAddress,
                    RemoteServerPortNumber,
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
                    MyLocalIpAddress.ToString(),
                    MyServerPortNumber);

            var sendRequestStartedEvent = new ServerEvent
            {
                EventType = sendRequestStartedEventType,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                LocalIpAddress = MyLocalIpAddress,
                LocalPortNumber = MyServerPortNumber
            };

            var sendRequestCompleteEvent = new ServerEvent
            {
                EventType = sendRequestCompleteEventType,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                LocalIpAddress = MyLocalIpAddress,
                LocalPortNumber = MyServerPortNumber
            };

            var outboundRequest =
                new ServerRequestController(_requestId, BufferSize, SocketTimeoutInMilliseconds)
                {
                    RemoteServerInfo = RemoteServerInfo
                };

            outboundRequest.EventOccurred += HandleEventOccurred;
            outboundRequest.SocketEventOccurred += HandleSocketEventOccurred;

            lock (QueueLock)
            {
                _requests.Add(outboundRequest);
                _requestId++;
            }

            return
                outboundRequest.SendServerRequestAsync(
                    requestBytes,
                    RemoteServerSessionIpAddress,
                    RemoteServerPortNumber,
                    sendRequestStartedEvent,
                    sendRequestCompleteEvent);
        }

        public Task<Result> RequestFileListAsync(
            IPAddress remoteServerIpAddress,
            int remoteServerPort,
            string targetFolder)
        {
            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);
            RemoteServerTransferFolderPath = targetFolder;

            var requestBytes =
                ServerRequestDataBuilder.ConstructRequestWithStringValue(
                    ServerRequestType.FileListRequest,
                    MyLocalIpAddress.ToString(),
                    MyServerPortNumber,
                    RemoteServerTransferFolderPath);

            var sendRequestStartedEvent = new ServerEvent
            {
                EventType = ServerEventType.RequestFileListStarted,
                LocalIpAddress = MyLocalIpAddress,
                LocalPortNumber = MyServerPortNumber,
                RemoteServerIpAddress = remoteServerIpAddress,
                RemoteServerPortNumber = remoteServerPort,
                RemoteFolder = targetFolder
            };

            var sendRequestCompleteEvent =
                new ServerEvent {EventType = ServerEventType.RequestFileListComplete};

            var outboundRequest =
                new ServerRequestController(_requestId, BufferSize, SocketTimeoutInMilliseconds)
                {
                    RemoteServerInfo = RemoteServerInfo
                };

            outboundRequest.EventOccurred += HandleEventOccurred;
            outboundRequest.SocketEventOccurred += HandleSocketEventOccurred;

            lock (QueueLock)
            {
                _requests.Add(outboundRequest);
                _requestId++;
            }

            return
                outboundRequest.SendServerRequestAsync(
                    requestBytes,
                    remoteServerIpAddress,
                    remoteServerPort,
                    sendRequestStartedEvent,
                    sendRequestCompleteEvent);
        }

        async Task<Result> SendFileListAsync(ServerRequestController inboundRequest)
        {
            var getLocalFolderPath = inboundRequest.GetLocalFolderPath();
            if (getLocalFolderPath.Failure)
            {
                return getLocalFolderPath;
            }

            MyTransferFolderPath = getLocalFolderPath.Value;

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceivedFileListRequest,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                LocalFolder = MyTransferFolderPath,
                RequestId = inboundRequest.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            if (!Directory.Exists(MyTransferFolderPath))
            {
                return
                    await SendBasicServerRequestAsync(
                        ServerRequestType.RequestedFolderDoesNotExist,
                        ServerEventType.SendNotificationFolderDoesNotExistStarted,
                        ServerEventType.SendNotificationFolderDoesNotExistComplete).ConfigureAwait(false);
            }

            var fileInfoList = new FileInfoList(MyTransferFolderPath);
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
                    MyLocalIpAddress.ToString(),
                    MyServerPortNumber,
                    MyTransferFolderPath);

            var sendRequestStartedEvent = new ServerEvent
            {
                EventType = ServerEventType.SendFileListStarted,
                LocalIpAddress = MyLocalIpAddress,
                LocalPortNumber = MyServerPortNumber,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                RemoteServerFileList = fileInfoList,
                LocalFolder = MyTransferFolderPath
            };

            var sendRequestCompleteEvent =
                new ServerEvent {EventType = ServerEventType.SendFileListComplete};

            var outboundRequest =
                new ServerRequestController(_requestId, BufferSize, SocketTimeoutInMilliseconds)
                {
                    RemoteServerInfo = RemoteServerInfo
                };

            outboundRequest.EventOccurred += HandleEventOccurred;
            outboundRequest.SocketEventOccurred += HandleSocketEventOccurred;

            lock (QueueLock)
            {
                _requests.Add(outboundRequest);
                _requestId++;
            }

            return
                await outboundRequest.SendServerRequestAsync(
                    requestBytes,
                    RemoteServerSessionIpAddress,
                    RemoteServerPortNumber,
                    sendRequestStartedEvent,
                    sendRequestCompleteEvent).ConfigureAwait(false);
        }

        void HandleRequestedFolderDoesNotExist(ServerRequestController inboundRequest)
        {
            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceivedNotificationFolderDoesNotExist,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                RequestId = inboundRequest.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());
        }

        void HandleNoFilesAvailableForDownload(ServerRequestController inboundRequest)
        {
            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceivedNotificationNoFilesToDownload,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
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
                LocalIpAddress = MyLocalIpAddress,
                LocalPortNumber = MyServerPortNumber,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                RemoteFolder = RemoteServerTransferFolderPath,
                RemoteServerFileList = RemoteServerFileList,
                RequestId = inboundRequest.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());
        }

        public Task<Result> RequestServerInfoAsync(
            IPAddress remoteServerIpAddress,
            int remoteServerPort)
        {
            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);

            return
                SendBasicServerRequestAsync(
                    ServerRequestType.ServerInfoRequest,
                    ServerEventType.RequestServerInfoStarted,
                    ServerEventType.RequestServerInfoComplete);
        }

        Task<Result> SendServerInfoAsync(ServerRequestController inboundRequest)
        {
            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceivedServerInfoRequest,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                RequestId = inboundRequest.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            var requestBytes =
                ServerRequestDataBuilder.ConstructServerInfoResponse(
                    MyLocalIpAddress.ToString(),
                    MyServerPortNumber,
                    Platform,
                    MyPublicIpAddress.ToString(),
                    MyTransferFolderPath);

            var sendRequestStartedEvent = new ServerEvent
            {
                EventType = ServerEventType.SendServerInfoStarted,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                RemoteServerPlatform = Platform,
                LocalFolder = MyTransferFolderPath,
                LocalIpAddress = MyLocalIpAddress,
                LocalPortNumber = MyServerPortNumber,
                PublicIpAddress = MyPublicIpAddress
            };

            var sendRequestCompleteEvent =
                new ServerEvent {EventType = ServerEventType.SendServerInfoComplete};

            var outboundRequest =
                new ServerRequestController(_requestId, BufferSize, SocketTimeoutInMilliseconds)
                {
                    RemoteServerInfo = RemoteServerInfo
                };

            outboundRequest.EventOccurred += HandleEventOccurred;
            outboundRequest.SocketEventOccurred += HandleSocketEventOccurred;

            lock (QueueLock)
            {
                _requests.Add(outboundRequest);
                _requestId++;
            }

            return
                outboundRequest.SendServerRequestAsync(
                    requestBytes,
                    RemoteServerSessionIpAddress,
                    RemoteServerPortNumber,
                    sendRequestStartedEvent,
                    sendRequestCompleteEvent);
        }

        void ReceiveServerInfo(ServerRequestController inboundRequest)
        {
            DetermineRemoteServerSessionIpAddress();

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceivedServerInfo,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                RemoteServerPlatform = RemoteServerInfo.Platform,
                RemoteFolder = RemoteServerTransferFolderPath,
                LocalIpAddress = RemoteServerLocalIpAddress,
                PublicIpAddress = RemoteServerPublicIpAddress,
                RequestId = inboundRequest.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());
        }

        void DetermineRemoteServerSessionIpAddress()
        {
            var checkLocalIp = RemoteServerLocalIpAddress.IsInRange(_myLanCidrIp);
            if (checkLocalIp.Failure)
            {
                RemoteServerInfo.SessionIpAddress = RemoteServerPublicIpAddress;
            }

            var remoteServerIsInMyLan = checkLocalIp.Value;

            RemoteServerInfo.SessionIpAddress = remoteServerIsInMyLan
                ? RemoteServerLocalIpAddress
                : RemoteServerPublicIpAddress;
        }

        public async Task<Result> ShutdownAsync()
        {
            if (!ServerIsListening)
            {
                return Result.Fail("Server is already shutdown");
            }

            //TODO: This looks awkward, change how shutdown command is sent to local server
            RemoteServerInfo = Info;

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
            if (Info.IsEqualTo(RemoteServerInfo))
            {
                ShutdownInitiated = true;
            }

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceivedShutdownServerCommand,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
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