using AaronLuna.AsyncFileServer.Utilities;

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
        readonly List<TextSession> _textSessions;
        readonly List<ServerRequestController> _requestQueue;
        readonly List<ServerRequestController> _requestArchive;
        readonly List<FileTransferController> _fileTransfers;
        readonly Socket _listenSocket;

        CancellationToken _token;
        static readonly object FileLock = new object();

        bool ServerIsInitialized
        {
            get => (Interlocked.CompareExchange(ref _initialized, 1, 1) == 1);
            set
            {
                if (value) Interlocked.CompareExchange(ref _initialized, 1, 0);
                else Interlocked.CompareExchange(ref _initialized, 0, 1);
            }
        }

        bool ServerIsListening
        {
            get => (Interlocked.CompareExchange(ref _listening, 1, 1) == 1);
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
            ServerIsInitialized = false;
            ServerIsListening = false;
            ServerIsBusy = false;
            InboundFileTransferStalled = false;
            OutboundFileTransferStalled = false;
            ShutdownInitiated = false;

            SocketSettings = new SocketSettings
            {
                ListenBacklogSize = 5,
                BufferSize = 1024,
                SocketTimeoutInMilliseconds = 5000
            };

            Info = new ServerInfo()
            {
                TransferFolder = GetDefaultTransferFolder()
            };

            RemoteServerInfo = new ServerInfo();
            RemoteServerFileList = new FileInfoList();
            
            _textSessionId = 1;
            _requestId = 1;
            _fileTransferId = 1;
            _myLanCidrIp = string.Empty;
            _listenSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _eventLog = new List<ServerEvent>();
            _textSessions = new List<TextSession>();
            _requestQueue = new List<ServerRequestController>();
            _requestArchive = new List<ServerRequestController>();
            _fileTransfers = new List<FileTransferController>();
        }

        public bool IsInitialized => ServerIsInitialized;
        public bool IsListening => ServerIsListening;
        public bool IsBusy => ServerIsBusy;

        public bool QueueIsEmpty => _requestQueue.Count == 0;
        public int RequestsInQueue => _requestQueue.Count;
        public ServerRequestController OldestRequestInQueue => _requestQueue.First();

        public bool NoFileTransfers => _fileTransfers.Count == 0;
        public List<int> FileTransferIds => _fileTransfers.Select(t => t.FiletransferId).ToList();
        public int OldestTransferId => _fileTransfers.First().FiletransferId;
        public int NewestTransferId => _fileTransfers.Last().FiletransferId;

        public List<int> StalledTransfersIds =>
            _fileTransfers.Select(t => t)
                .Where(t => t.TransferStalled)
                .Select(t => t.FiletransferId).ToList();

        public float TransferUpdateInterval { get; set; }
        public int TransferRetryLimit { get; set; }
        public TimeSpan RetryLimitLockout { get; set; }

        public SocketSettings SocketSettings { get; set; }
        public int ListenBacklogSize => SocketSettings.ListenBacklogSize;
        public int BufferSize => SocketSettings.BufferSize;
        public int SocketTimeoutInMilliseconds => SocketSettings.SocketTimeoutInMilliseconds;

        public PlatformID OperatingSystem => Environment.OSVersion.Platform;
        public ServerInfo Info { get; set; }

        public string MyTransferFolderPath
        {
            get => Info.TransferFolder;
            set => Info.TransferFolder = value;
        }

        public IPAddress MyLocalIpAddress => Info.LocalIpAddress;
        public IPAddress MyPublicIpAddress => Info.PublicIpAddress;
        public int MyServerPortNumber => Info.PortNumber;

        public ServerInfo RemoteServerInfo { get; set; }

        public string RemoteServerTransferFolderPath
        {
            get => RemoteServerInfo.TransferFolder;
            set => RemoteServerInfo.TransferFolder = value;
        }

        public IPAddress RemoteServerSessionIpAddress => RemoteServerInfo.SessionIpAddress;
        public IPAddress RemoteServerLocalIpAddress => RemoteServerInfo.LocalIpAddress;
        public IPAddress RemoteServerPublicIpAddress => RemoteServerInfo.PublicIpAddress;
        public int RemoteServerPortNumber => RemoteServerInfo.PortNumber;

        public FileInfoList RemoteServerFileList { get; set; }

        public bool FileTransferAccepted => RemoteServerAcceptedFileTransfer;
        public bool FileTransferRejected => RemoteServerRejectedFileTransfer;
        public bool FileTransferInProgress => TransferInProgress;
        public bool FileTransferCanceled => OutboundFileTransferStalled;
        public bool FileTransferStalled => InboundFileTransferStalled;
        
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

        public Result<ServerRequestController> GetServerRequestById(int id)
        {
            var matches = _requestQueue.Select(r => r).Where(r => r.Request.Id == id).ToList();

            if (matches.Count == 0)
            {
                return Result.Fail<ServerRequestController>($"No request was found with an ID value of {id}");
            }

            if (matches.Count > 1)
            {
                return Result.Fail<ServerRequestController>($"Found {matches.Count} requests with the same ID value of {id}");
            }

            var matchingRequest = matches[0];
            matchingRequest.Request.EventLog = _eventLog.Select(e => e).Where(e => e.RequestId == matchingRequest.Request.Id).ToList();

            return Result.Ok(matchingRequest);
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

            EventOccurred?.Invoke(this,
             new ServerEvent { EventType = ServerEventType.ServerStoppedListening });

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

                var requestController = new ServerRequestController(Info, BufferSize, SocketTimeoutInMilliseconds);
                requestController.EventOccurred += HandleEventOccurred;
                requestController.SocketEventOccurred += HandleSocketEventOccurred;

                var receiveServerRequest = await requestController.ReceiveServerRequestAsync(socket);
                if (receiveServerRequest.Failure)
                {
                    return receiveServerRequest;
                }

                var incomingRequest = receiveServerRequest.Value;
                incomingRequest.Id = _requestId;
                _requestQueue.Add(requestController);
                _requestId++;

                if (_token.IsCancellationRequested || ShutdownInitiated) return Result.Ok();
                if (requestController.InboundFileTransferRequested) continue;
                if (ServerIsBusy) continue;

                var processIncomingRequest = await ProcessRequestAsync(requestController.RequestId);
                if (processIncomingRequest.Failure)
                {
                    return processIncomingRequest;
                }
            }
        }
        
        public Task<Result> ProcessNextRequestInQueueAsync()
        {
            return ProcessRequestAsync(OldestRequestInQueue.RequestId);
        }

        public async Task<Result> ProcessRequestAsync(int requestId)
        {
            if (QueueIsEmpty)
            {
                return Result.Fail("Queue is empty");
            }

            if (ServerIsBusy)
            {
                return Result.Fail("Server is busy, please try again after the current operation has completed");
            }

            var checkQueue = _requestQueue.Select(m => m).Where(m => m.RequestId == requestId).ToList();
            var checkArchive = _requestArchive.Select(m => m).Where(m => m.RequestId == requestId).ToList();

            if (checkQueue.Count == 0 && checkArchive.Count == 1)
            {
                return Result.Fail($"ServerRequest ID# {requestId} has already been processed, event logs for this request are available in the archive.");
            }

            if (checkQueue.Count == 0 && checkArchive.Count == 0)
            {
                return Result.Fail($"ServerRequest ID# {requestId} appears to be invalid. No record of this request were found in the queue or the archive.");
            }

            if (checkQueue.Count != 1) return Result.Fail($"Unable to determine if request ID# {requestId} is valid");

            var requestController = checkQueue[0];
            _requestQueue.Remove(requestController);
            _requestArchive.Add(requestController);

            return await ProcessRequestAsync(requestController);
        }

        async Task<Result> ProcessRequestAsync(ServerRequestController requestController)
        {
            ServerIsBusy = true;
            RemoteServerInfo = requestController.RemoteServerInfo;
            RemoteServerTransferFolderPath = requestController.RemoteServerInfo.TransferFolder;
            
            EventOccurred?.Invoke(this, new ServerEvent
            {
                EventType = ServerEventType.ProcessRequestStarted,
                RequestType = requestController.Request.Type,
                RequestId = requestController.RequestId
            });

            var result = Result.Ok();

            switch (requestController.Request.Type)
            {
                case ServerRequestType.TextMessage:
                    result = ReceiveTextMessage(requestController);
                    break;

                case ServerRequestType.InboundFileTransferRequest:
                    result = await HandleInboundFileTransferRequestAsync(requestController, _token).ConfigureAwait(false);
                    break;

                case ServerRequestType.OutboundFileTransferRequest:
                    result = await HandleOutboundFileTransferRequestAsync(requestController).ConfigureAwait(false);
                    break;

                case ServerRequestType.FileTransferAccepted:
                    result = await HandleFileTransferAcceptedAsync(requestController, _token).ConfigureAwait(false);
                    break;

                case ServerRequestType.FileTransferComplete:
                    result = HandleFileTransferCompleted(requestController);
                    break;

                case ServerRequestType.FileTransferStalled:
                    result = HandleStalledFileTransfer(requestController);
                    break;

                case ServerRequestType.RetryOutboundFileTransfer:
                    result = await HandleRetryFileTransferAsync(requestController).ConfigureAwait(false);
                    break;

                case ServerRequestType.RetryLimitExceeded:
                    result = HandleRetryLimitExceeded(requestController);
                    break;

                case ServerRequestType.FileListRequest:
                    result = await SendFileListAsync(requestController).ConfigureAwait(false);
                    break;

                case ServerRequestType.FileListResponse:
                    ReceiveFileList(requestController);
                    break;

                case ServerRequestType.NoFilesAvailableForDownload:
                    HandleNoFilesAvailableForDownload(requestController.Request);
                    break;

                case ServerRequestType.FileTransferRejected:
                    result = HandleFileTransferRejected(requestController);
                    break;

                case ServerRequestType.RequestedFolderDoesNotExist:
                    HandleRequestedFolderDoesNotExist(requestController.Request);
                    break;

                case ServerRequestType.RequestedFileDoesNotExist:
                    result = HandleRequestedFileDoesNotExist(requestController);
                    break;

                case ServerRequestType.ServerInfoRequest:
                    result = await SendServerInfoAsync(requestController.Request).ConfigureAwait(false);
                    break;

                case ServerRequestType.ServerInfoResponse:
                    ReceiveServerInfo(requestController.Request);
                    break;

                case ServerRequestType.ShutdownServerCommand:
                    HandleShutdownServerCommand(requestController.Request);
                    break;

                default:
                    var error = $"Unable to determine transfer type, value of '{requestController.Request.Type}' is invalid.";
                    return Result.Fail(error);
            }

            requestController.ShutdownSocket();
            ServerIsBusy = false;

            if (result.Failure)
            {
                EventOccurred?.Invoke(this, new ServerEvent
                {
                    EventType = ServerEventType.ErrorOccurred,
                    ErrorMessage = result.Error
                });
                
                return result;
            }

            EventOccurred?.Invoke(this, new ServerEvent
            {
                EventType = ServerEventType.ProcessRequestComplete,
                RequestType = requestController.Request.Type,
                RequestId = requestController.RequestId,
                RemoteServerIpAddress = RemoteServerSessionIpAddress
            });

            var handleQueuedRequests = await HandleRemainingRequestsInQueue();
            if (handleQueuedRequests.Failure)
            {
                return handleQueuedRequests;
            }

            return Result.Ok();
        }

        void HandleEventOccurred(object sender, ServerEvent e)
        {
            EventOccurred?.Invoke(sender, e);
        }

        void HandleSocketEventOccurred(object sender, ServerEvent e)
        {
            SocketEventOccurred?.Invoke(sender, e);
        }

        async Task<Result> HandleRemainingRequestsInQueue()
        {
            foreach (var request in _requestQueue)
            {
                if (!request.ProcessRequestImmediately) continue;

                var result = await ProcessRequestAsync(request.RequestId);
                if (result.Failure)
                {
                    return result;
                }
            }

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
            var textSessionId = GetTextSessionIdForRemoteServer(RemoteServerInfo);

            var newMessage = new TextMessage
            {
                SessionId = textSessionId,
                TimeStamp = DateTime.Now,
                Author = TextMessageAuthor.Self,
                Message = message
            };

            var textSession = GetTextSessionById(textSessionId).Value;
            textSession.Messages.Add(newMessage);

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = ServerEventType.SendTextMessageStarted,
                    TextMessage = message,
                    TextSessionId = textSessionId,
                    RemoteServerIpAddress = RemoteServerSessionIpAddress,
                    RemoteServerPortNumber = RemoteServerPortNumber
                });

            var connectResult = await ConnectToServerAsync().ConfigureAwait(false);
            if (connectResult.Failure)
            {
                return connectResult;
            }

            var socket = connectResult.Value;
            var messageData =
                ServerRequestDataBuilder.ConstructRequestWithStringValue(
                    ServerRequestType.TextMessage,
                    MyLocalIpAddress.ToString(),
                    MyServerPortNumber,
                    message);

            var sendMessageDataResult = await SendMessageData(socket, messageData).ConfigureAwait(false);
            if (sendMessageDataResult.Failure)
            {
                return sendMessageDataResult;
            }

            CloseSocket(socket);

            EventOccurred?.Invoke(this,
                new ServerEvent
                    {EventType = ServerEventType.SendTextMessageComplete});

            return Result.Ok();
        }

        Result ReceiveTextMessage(ServerRequestController requestController)
        {
            var getTextMessage = requestController.GetTextMessage();
            if (getTextMessage.Failure)
            {
                return getTextMessage;
            }

            var newMessage = getTextMessage.Value;

            var textSessionId = GetTextSessionIdForRemoteServer(requestController.RemoteServerInfo);
            var textSession = GetTextSessionById(textSessionId).Value;
            newMessage.SessionId = textSessionId;
            textSession.Messages.Add(newMessage);

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceivedTextMessage,
                TextMessage = newMessage.Message,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                TextSessionId = textSessionId,
                RequestId = requestController.RequestId
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            return Result.Ok();
        }

        public async Task<Result> SendFileAsync(
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

            var sendRequestResult = await SendOutboundFileTransferRequestAsync(outboundFileTransfer).ConfigureAwait(false);
            if (sendRequestResult.Success) return Result.Ok();

            outboundFileTransfer.Status = FileTransferStatus.Error;
            outboundFileTransfer.ErrorMessage = sendRequestResult.Error;

            return sendRequestResult;
        }

        async Task<Result> HandleOutboundFileTransferRequestAsync(ServerRequestController requestController)
        {
            var getFileTransfer = requestController.GetOutboundFileTransfer();
            if (getFileTransfer.Failure)
            {
                return getFileTransfer;
            }

            var outboundFileTransfer = getFileTransfer.Value;

            if (!File.Exists(outboundFileTransfer.LocalFilePath))
            {
                return await SendFileTransferResponse(
                    ServerRequestType.RequestedFileDoesNotExist,
                    0,
                    outboundFileTransfer.RemoteServerTransferId,
                    ServerEventType.SendNotificationFileDoesNotExistStarted,
                    ServerEventType.SendNotificationFileDoesNotExistComplete).ConfigureAwait(false);
            }

            // TODO: Create logic to check stalled file transfers that are under lockout and if this request matches remoteserver info + localfilepath, send a new filetranserresponse = rejected_retrylimitexceeded. maybe we should penalize them for trying to subvert our lockout policy?
            outboundFileTransfer.Id = _fileTransferId;            
            _fileTransferId++;

            var fileTransferController = new FileTransferController(outboundFileTransfer);
            _fileTransfers.Add(fileTransferController);

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceivedOutboundFileTransferRequest,
                LocalFolder = Path.GetDirectoryName(outboundFileTransfer.LocalFilePath),
                FileName = Path.GetFileName(outboundFileTransfer.LocalFilePath),
                FileSizeInBytes = new FileInfo(outboundFileTransfer.LocalFilePath).Length,
                RemoteFolder = RemoteServerTransferFolderPath,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                RequestId = requestController.RequestId,
                FileTransferId = outboundFileTransfer.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            var sendRequestResult = await SendOutboundFileTransferRequestAsync(outboundFileTransfer).ConfigureAwait(false);
            if (sendRequestResult.Success) return Result.Ok();

            outboundFileTransfer.Status = FileTransferStatus.Error;
            outboundFileTransfer.ErrorMessage = sendRequestResult.Error;

            return sendRequestResult;
        }

        async Task<Result> SendOutboundFileTransferRequestAsync(FileTransfer outboundFileTransfer)
        {
            RemoteServerAcceptedFileTransfer = false;
            RemoteServerRejectedFileTransfer = false;

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.RequestOutboundFileTransferStarted,
                LocalIpAddress = MyLocalIpAddress,
                LocalPortNumber = MyServerPortNumber,
                LocalFolder = outboundFileTransfer.LocalFolderPath,
                FileName = Path.GetFileName(outboundFileTransfer.LocalFilePath),
                FileSizeInBytes = outboundFileTransfer.FileSizeInBytes,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                RemoteFolder = RemoteServerTransferFolderPath,
                FileTransferId = outboundFileTransfer.Id
            });

            var messageData =
                ServerRequestDataBuilder.ConstructOutboundFileTransferRequest(
                    MyLocalIpAddress.ToString(),
                    MyServerPortNumber,
                    outboundFileTransfer);

            EventOccurred?.Invoke(this, _eventLog.Last());

            var connectResult = await ConnectToServerAsync().ConfigureAwait(false);
            if (connectResult.Failure)
            {
                return connectResult;
            }

            outboundFileTransfer.SendSocket = connectResult.Value;
            var sendMessageDataResult = await SendMessageData(outboundFileTransfer.SendSocket, messageData).ConfigureAwait(false);
            if (sendMessageDataResult.Failure)
            {
                return sendMessageDataResult;
            }

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.RequestOutboundFileTransferComplete,
                LocalIpAddress = MyLocalIpAddress,
                LocalPortNumber = MyServerPortNumber,
                LocalFolder = outboundFileTransfer.LocalFolderPath,
                FileName = Path.GetFileName(outboundFileTransfer.LocalFilePath),
                FileSizeInBytes = outboundFileTransfer.FileSizeInBytes,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                RemoteFolder = RemoteServerTransferFolderPath,
                FileTransferId = outboundFileTransfer.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            return Result.Ok();
        }

        Result HandleFileTransferRejected(ServerRequestController requestController)
        {
            RemoteServerRejectedFileTransfer = true;

            var getResponseCode = requestController.GetFileTransferResponseCode();
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

            //TODO: Investigate why this causes a bug that fails my unit tests
            //OutgoingFilePath = string.Empty;
            //RemoteServerTransferFolderPath = string.Empty;

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.RemoteServerRejectedFileTransfer,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                RequestId = requestController.RequestId,
                FileTransferId = outboundFileTransfer.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());
            TransferInProgress = false;

            CloseSocket(outboundFileTransfer.SendSocket);

            return Result.Ok();
        }

        async Task<Result> HandleFileTransferAcceptedAsync(
            ServerRequestController requestController,
            CancellationToken token)
        {
            RemoteServerAcceptedFileTransfer = true;
            TransferInProgress = true;

            var getResponseCode = requestController.GetFileTransferResponseCode();
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
            outboundFileTransfer.Status = FileTransferStatus.Accepted;

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.RemoteServerAcceptedFileTransfer,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                RequestId = requestController.RequestId,
                FileTransferId = outboundFileTransfer.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            var sendFileBytesResult = 
                await SendFileBytesAsync(outboundFileTransfer, requestController.RequestId, token);

            if (sendFileBytesResult.Success) return Result.Ok();

            outboundFileTransfer.Status = FileTransferStatus.Error;
            outboundFileTransfer.ErrorMessage = sendFileBytesResult.Error;
            outboundFileTransfer.TransferCompleteTime = DateTime.Now;

            CloseSocket(outboundFileTransfer.SendSocket);
            TransferInProgress = false;

            return sendFileBytesResult;
        }

        async Task<Result> SendFileBytesAsync(
            FileTransfer fileTransfer,
            int requestId,
            CancellationToken token)
        {
            fileTransfer.Status = FileTransferStatus.InProgress;
            fileTransfer.TransferStartTime = DateTime.Now;

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.SendFileBytesStarted,
                RemoteServerIpAddress = fileTransfer.RemoteServerIpAddress,
                RemoteServerPortNumber = fileTransfer.RemoteServerPortNumber,
                RequestId = requestId,
                FileTransferId = fileTransfer.Id
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
                    SocketEventOccurred?.Invoke(this,
                        new ServerEvent
                        {
                            EventType = ServerEventType.SentFileChunkToClient,
                            FileSizeInBytes = fileTransfer.FileSizeInBytes,
                            CurrentFileBytesSent = fileChunkSize,
                            FileBytesRemaining = fileTransfer.BytesRemaining,
                            FileChunkSentCount = fileTransfer.FileChunkSentCount,
                            SocketSendCount = socketSendCount
                        });
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
                    RequestId = requestId,
                    FileTransferId = fileTransfer.Id
                });

                EventOccurred?.Invoke(this, _eventLog.Last());

                CloseSocket(fileTransfer.SendSocket);
                TransferInProgress = false;

                return Result.Ok();
            }
        }

        Result HandleFileTransferCompleted(ServerRequestController requestController)
        {
            var getResponseCode = requestController.GetFileTransferResponseCode();
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

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.RemoteServerConfirmedFileTransferCompleted,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                RequestId = requestController.RequestId,
                FileTransferId = outboundFileTransfer.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            return Result.Ok();
        }

        public async Task<Result> GetFileAsync(
            string remoteServerIpAddress,
            int remoteServerPort,
            string remoteFilePath,
            string localFolderPath)
        {
            var inboundFileTransfer = new FileTransfer(BufferSize)
            {
                Id = _fileTransferId,
                TransferDirection = FileTransferDirection.Inbound,
                Initiator = FileTransferInitiator.Self,
                Status = FileTransferStatus.AwaitingResponse,
                MyLocalIpAddress = MyLocalIpAddress,
                MyPublicIpAddress = MyPublicIpAddress,
                MyServerPortNumber = MyServerPortNumber,
                RemoteServerIpAddress = NetworkUtilities.ParseSingleIPv4Address(remoteServerIpAddress).Value,
                RemoteServerPortNumber = remoteServerPort,
                LocalFilePath = Path.Combine(localFolderPath, Path.GetFileName(remoteFilePath)),
                LocalFolderPath = localFolderPath,
                RemoteFolderPath = Path.GetDirectoryName(remoteFilePath),
                RemoteFilePath = remoteFilePath,
                RequestInitiatedTime = DateTime.Now
            };

            var fileTransferController = new FileTransferController(inboundFileTransfer);

            _fileTransfers.Add(fileTransferController);
            _fileTransferId++;

            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);
            RemoteServerTransferFolderPath = Path.GetDirectoryName(remoteFilePath);
            MyTransferFolderPath = localFolderPath;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = ServerEventType.RequestInboundFileTransferStarted,
                    RemoteServerIpAddress = RemoteServerSessionIpAddress,
                    RemoteServerPortNumber = RemoteServerPortNumber,
                    RemoteFolder = RemoteServerTransferFolderPath,
                    FileName = Path.GetFileName(remoteFilePath),
                    LocalIpAddress = MyLocalIpAddress,
                    LocalPortNumber = MyServerPortNumber,
                    LocalFolder = MyTransferFolderPath,
                    FileTransferId = inboundFileTransfer.Id
                });

            var messageData =
                ServerRequestDataBuilder.ConstructInboundFileTransferRequest(
                    MyLocalIpAddress.ToString(),
                    MyServerPortNumber,
                    inboundFileTransfer);

            var connectResult = await ConnectToServerAsync().ConfigureAwait(false);
            if (connectResult.Failure)
            {
                inboundFileTransfer.Status = FileTransferStatus.Error;
                inboundFileTransfer.ErrorMessage = connectResult.Error;

                return connectResult;
            }

            var socket = connectResult.Value;
            var sendMessageDataResult = await SendMessageData(socket, messageData).ConfigureAwait(false);
            if (sendMessageDataResult.Failure)
            {
                inboundFileTransfer.Status = FileTransferStatus.Error;
                inboundFileTransfer.ErrorMessage = connectResult.Error;

                return sendMessageDataResult;
            }

            CloseSocket(socket);

            EventOccurred?.Invoke(this,
                new ServerEvent
                    {EventType = ServerEventType.RequestInboundFileTransferComplete});

            return Result.Ok();
        }

        Result HandleRequestedFileDoesNotExist(ServerRequestController requestController)
        {
            var getFileTransferId = requestController.GetRemoteServerFileTransferId();
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

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceivedNotificationFileDoesNotExist,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                RequestId = requestController.RequestId,
                FileTransferId = inboundFileTransfer.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());
            TransferInProgress = false;

            return Result.Ok();
        }

        async Task<Result> HandleInboundFileTransferRequestAsync(ServerRequestController requestController, CancellationToken token)
        {
            var getInboundFileTransfer = GetInboundFileTransfer(requestController);
            if (getInboundFileTransfer.Failure)
            {
                return getInboundFileTransfer;
            }

            var inboundFileTransfer = getInboundFileTransfer.Value;

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
                RequestId = requestController.RequestId,
                FileTransferId = inboundFileTransfer.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            if (File.Exists(inboundFileTransfer.LocalFilePath))
            {
                inboundFileTransfer.Status = FileTransferStatus.Rejected;
                inboundFileTransfer.TransferStartTime = DateTime.Now;
                inboundFileTransfer.TransferCompleteTime = inboundFileTransfer.TransferStartTime;

                return await SendFileTransferResponse(
                        ServerRequestType.FileTransferRejected,
                        inboundFileTransfer.Id,
                        inboundFileTransfer.TransferResponseCode,
                        ServerEventType.SendFileTransferRejectedStarted,
                        ServerEventType.SendFileTransferRejectedComplete).ConfigureAwait(false);
            }

            inboundFileTransfer.Status = FileTransferStatus.Accepted;

            var acceptTransferResult =
                await SendFileTransferResponse(
                    ServerRequestType.FileTransferAccepted,
                    inboundFileTransfer.Id,
                    inboundFileTransfer.TransferResponseCode,
                    ServerEventType.SendFileTransferAcceptedStarted,
                    ServerEventType.SendFileTransferAcceptedComplete).ConfigureAwait(false);

            if (acceptTransferResult.Failure)
            {
                inboundFileTransfer.Status = FileTransferStatus.Error;
                inboundFileTransfer.ErrorMessage = acceptTransferResult.Error;

                return acceptTransferResult;
            }

            var receiveFileResult = await ReceiveFileAsync(requestController, inboundFileTransfer, token).ConfigureAwait(false);
            if (receiveFileResult.Success) return Result.Ok();

            inboundFileTransfer.Status = FileTransferStatus.Error;
            inboundFileTransfer.ErrorMessage = receiveFileResult.Error;

            return receiveFileResult;
        }

        Result<FileTransfer> GetInboundFileTransfer(ServerRequestController requestController)
        {
            FileTransfer inboundFileTransfer;
            var getInboundFileTransferId = requestController.GetInboundFileTransferId();
            if (getInboundFileTransferId.Success)
            {
                var fileTransferId = getInboundFileTransferId.Value;
                var getFileTransferController = GetFileTransferById(fileTransferId);
                if (getFileTransferController.Failure)
                {
                    return Result.Fail<FileTransfer>(getFileTransferController.Error);
                }

                inboundFileTransfer = getFileTransferController.Value.FileTransfer;

                var syncFileTransfer = requestController.SynchronizeFileTransferDetails(inboundFileTransfer);
                if (syncFileTransfer.Failure)
                {
                    return Result.Fail<FileTransfer>(syncFileTransfer.Error);
                }

                inboundFileTransfer = syncFileTransfer.Value;
            }
            else
            {
                var getInboundFileTransfer = requestController.GetInboundFileTransfer();
                if (getInboundFileTransfer.Failure)
                {
                    return getInboundFileTransfer;
                }

                inboundFileTransfer = getInboundFileTransfer.Value;
                inboundFileTransfer.Id = _fileTransferId;

                var fileTransferController = new FileTransferController(inboundFileTransfer);
                _fileTransfers.Add(fileTransferController);
                _fileTransferId++;
            }

            return Result.Ok(inboundFileTransfer);
        }
        
        async Task<Result> ReceiveFileAsync(
            ServerRequestController requestController,
            FileTransfer fileTransfer,
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
                RequestId = requestController.RequestId,
                FileTransferId = fileTransfer.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            var receiveCount = 0;
            fileTransfer.TotalBytesReceived = 0;
            fileTransfer.BytesRemaining = fileTransfer.FileSizeInBytes;
            fileTransfer.PercentComplete = 0;
            InboundFileTransferStalled = false;

            if (requestController.UnreadBytes.Count > 0)
            {
                fileTransfer.TotalBytesReceived += requestController.UnreadBytes.Count;
                fileTransfer.BytesRemaining -= requestController.UnreadBytes.Count;

                lock (FileLock)
                {
                    var writeBytesResult =
                        FileHelper.WriteBytesToFile(
                            fileTransfer.LocalFilePath,
                            requestController.UnreadBytes.ToArray(),
                            requestController.UnreadBytes.Count,
                            10);

                    if (writeBytesResult.Failure)
                    {
                        return writeBytesResult;
                    }
                }

                _eventLog.Add(new ServerEvent
                {
                    EventType = ServerEventType.CopySavedBytesToIncomingFile,
                    CurrentFileBytesReceived = requestController.UnreadBytes.Count,
                    TotalFileBytesReceived = fileTransfer.TotalBytesReceived,
                    FileSizeInBytes = fileTransfer.FileSizeInBytes,
                    FileBytesRemaining = fileTransfer.BytesRemaining
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

                var readFromSocketResult =
                    await fileTransfer.ReceiveSocket.ReceiveWithTimeoutAsync(
                            fileTransfer.Buffer,
                            0,
                            BufferSize,
                            SocketFlags.None,
                            SocketTimeoutInMilliseconds)
                        .ConfigureAwait(false);

                if (readFromSocketResult.Failure)
                {
                    return readFromSocketResult;
                }

                fileTransfer.CurrentBytesReceived = readFromSocketResult.Value;
                var receivedBytes = new byte[fileTransfer.CurrentBytesReceived];

                if (fileTransfer.CurrentBytesReceived == 0)
                {
                    return Result.Fail("Socket is no longer receiving data, must abort file transfer");
                }

                int fileWriteAttempts;
                lock (FileLock)
                {
                    var writeBytesResult = FileHelper.WriteBytesToFile(
                        fileTransfer.LocalFilePath,
                        receivedBytes,
                        fileTransfer.CurrentBytesReceived,
                        10);

                    if (writeBytesResult.Failure)
                    {
                        return writeBytesResult;
                    }

                    fileWriteAttempts = writeBytesResult.Value + 1;
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
                        FileTransferId = fileTransfer.Id
                    });

                    EventOccurred?.Invoke(this, _eventLog.Last());
                }

                // this method fires on every socket read event, which could be hurdreds of thousands
                // of times depending on the file size and buffer size. Since this  event is only used
                // by myself when debugging small test files, I limited this event to only fire when 
                // the size of the file will result in less than 10 read events
                if (fileTransfer.FileSizeInBytes < 10 * BufferSize)
                {
                    SocketEventOccurred?.Invoke(this,
                    new ServerEvent
                    {
                        EventType = ServerEventType.ReceivedFileBytesFromSocket,
                        SocketReadCount = receiveCount,
                        BytesReceivedCount = fileTransfer.CurrentBytesReceived,
                        CurrentFileBytesReceived = fileTransfer.CurrentBytesReceived,
                        TotalFileBytesReceived = fileTransfer.TotalBytesReceived,
                        FileSizeInBytes = fileTransfer.FileSizeInBytes,
                        FileBytesRemaining = fileTransfer.BytesRemaining,
                        PercentComplete = fileTransfer.PercentComplete
                    });
                }

                // Report progress in intervals which are set by the user in the settings file
                if (changeSinceLastUpdate < TransferUpdateInterval) continue;

                fileTransfer.PercentComplete = checkPercentComplete;

                FileTransferProgress?.Invoke(this, new ServerEvent
                {
                    EventType = ServerEventType.UpdateFileTransferProgress,
                    TotalFileBytesReceived = fileTransfer.TotalBytesReceived,
                    PercentComplete = fileTransfer.PercentComplete,
                    RequestId = requestController.RequestId
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
                RequestId = requestController.RequestId,
                FileTransferId = fileTransfer.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            var confirmFileTransferResult =
                await SendFileTransferResponse(
                    ServerRequestType.FileTransferComplete,
                    fileTransfer.Id,
                    fileTransfer.TransferResponseCode,
                    ServerEventType.SendFileTransferCompletedStarted,
                    ServerEventType.SendFileTransferCompletedCompleted).ConfigureAwait(false);

            if (confirmFileTransferResult.Success) return Result.Ok();

            fileTransfer.Status = FileTransferStatus.Error;
            fileTransfer.ErrorMessage = confirmFileTransferResult.Error;

            return confirmFileTransferResult;
        }

        public async Task<Result> SendNotificationFileTransferStalledAsync(int fileTransferId)
        {
            const string fileTransferStalledErrorMessage =
                "Data is no longer bring received from remote client, file transfer has been canceled (SendNotificationFileTransferStalledAsync)";

            var getFileTransferResult = GetFileTransferById(fileTransferId);
            if (getFileTransferResult.Failure)
            {
                var error = $"{getFileTransferResult.Error} (AsyncFileServer.SendNotificationFileTransferStalledAsync)";
                return Result.Fail(error);
            }

            var inboundFileTransfer = getFileTransferResult.Value.FileTransfer;
            inboundFileTransfer.Status = FileTransferStatus.Stalled;
            inboundFileTransfer.TransferCompleteTime = DateTime.Now;
            inboundFileTransfer.ErrorMessage = fileTransferStalledErrorMessage;

            InboundFileTransferStalled = true;

            return await
                SendFileTransferResponse(
                    ServerRequestType.FileTransferStalled,
                    inboundFileTransfer.Id,
                    inboundFileTransfer.TransferResponseCode,
                    ServerEventType.SendFileTransferStalledStarted,
                    ServerEventType.SendFileTransferStalledComplete);
        }

        Result HandleStalledFileTransfer(ServerRequestController requestController)
        {
            const string fileTransferStalledErrorMessage =
                "Aborting file transfer, client says that data is no longer being received (HandleStalledFileTransfer)";

            var getResponseCode = requestController.GetFileTransferResponseCode();
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

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.FileTransferStalled,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                RequestId = requestController.RequestId,
                FileTransferId = outboundFileTransfer.Id
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
                SendFileTransferResponse(
                    ServerRequestType.RetryOutboundFileTransfer,
                    stalledFileTransfer.FiletransferId,
                    stalledFileTransfer.TransferResponseCode,
                    ServerEventType.RetryOutboundFileTransferStarted,
                    ServerEventType.RetryOutboundFileTransferComplete);
        }

        async Task<Result> HandleRetryFileTransferAsync(ServerRequestController requestController)
        {
            var getResponseCode = requestController.GetFileTransferResponseCode();
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

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceivedRetryOutboundFileTransferRequest,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                RequestId = requestController.RequestId,
                FileTransferId = canceledFileTransfer.Id
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

                return await SendRetryLimitExceeded(canceledFileTransfer);
            }

            canceledFileTransfer.ResetTransferValues();
            canceledFileTransfer.RemoteServerRetryLimit = TransferRetryLimit;

            var sendRequestResult = await SendOutboundFileTransferRequestAsync(canceledFileTransfer).ConfigureAwait(false);
            if (sendRequestResult.Success) return Result.Ok();

            canceledFileTransfer.Status = FileTransferStatus.Error;
            canceledFileTransfer.ErrorMessage = sendRequestResult.Error;

            return sendRequestResult;
        }

        async Task<Result> SendRetryLimitExceeded(FileTransfer outboundFileTransfer)
        {
            _eventLog.Add(new ServerEvent
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
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            var connectResult = await ConnectToServerAsync().ConfigureAwait(false);
            if (connectResult.Failure)
            {
                return connectResult;
            }

            var socket = connectResult.Value;
            var messageData =
                ServerRequestDataBuilder.ConstructRetryLimitExceededRequest(
                    MyLocalIpAddress.ToString(),
                    MyServerPortNumber,
                    outboundFileTransfer.RemoteServerTransferId,
                    TransferRetryLimit,
                    outboundFileTransfer.RetryLockoutExpireTime.Ticks);

            var sendMessageDataResult = await SendMessageData(socket, messageData).ConfigureAwait(false);
            if (sendMessageDataResult.Failure)
            {
                return sendMessageDataResult;
            }

            CloseSocket(socket);

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.SendRetryLimitExceededCompleted,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                LocalIpAddress = MyLocalIpAddress,
                LocalPortNumber = MyServerPortNumber,
                FileTransferId = outboundFileTransfer.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            return Result.Ok();
        }

        Result HandleRetryLimitExceeded(ServerRequestController requestController)
        {
            var getFileTransferId = requestController.GetRemoteServerFileTransferId();
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

            var updateFileTransfer = requestController.GetRetryLockoutDetails(inboundFileTransfer);
            if (updateFileTransfer.Failure)
            {
                return updateFileTransfer;
            }

            inboundFileTransfer = updateFileTransfer.Value;

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceiveRetryLimitExceeded,
                LocalFolder = inboundFileTransfer.LocalFolderPath,
                FileName = Path.GetFileName(inboundFileTransfer.LocalFilePath),
                FileSizeInBytes = inboundFileTransfer.FileSizeInBytes,
                RemoteServerIpAddress = inboundFileTransfer.RemoteServerIpAddress,
                RemoteServerPortNumber = inboundFileTransfer.RemoteServerPortNumber,
                RetryCounter = inboundFileTransfer.RetryCounter,
                RemoteServerRetryLimit = inboundFileTransfer.RemoteServerRetryLimit,
                RetryLockoutExpireTime = inboundFileTransfer.RetryLockoutExpireTime,
                RequestId = requestController.RequestId,
                FileTransferId = inboundFileTransfer.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            return Result.Ok();
        }

        async Task<Result> SendFileTransferResponse(
            ServerRequestType requestType,
            int fileTransferId,
            long responseCode,
            ServerEventType sendMessageStartedEventType,
            ServerEventType sendMessageCompleteEventType)
        {
            _eventLog.Add(new ServerEvent
            {
                EventType = sendMessageStartedEventType,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                LocalIpAddress = MyLocalIpAddress,
                LocalPortNumber = MyServerPortNumber,
                FileTransferId = fileTransferId
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            var connectResult = await ConnectToServerAsync().ConfigureAwait(false);
            if (connectResult.Failure)
            {
                return connectResult;
            }

            var socket = connectResult.Value;
            var messageData =
                ServerRequestDataBuilder.ConstructRequestWithInt64Value(
                    requestType,
                    MyLocalIpAddress.ToString(),
                    MyServerPortNumber,
                    responseCode);

            var sendMessageDataResult = await SendMessageData(socket, messageData).ConfigureAwait(false);
            if (sendMessageDataResult.Failure)
            {
                return sendMessageDataResult;
            }

            CloseSocket(socket);

            _eventLog.Add(new ServerEvent
            {
                EventType = sendMessageCompleteEventType,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                LocalIpAddress = MyLocalIpAddress,
                LocalPortNumber = MyServerPortNumber,
                FileTransferId = fileTransferId
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            return Result.Ok();
        }

        async Task<Result> SendSimpleMessageToClientAsync(
            ServerRequestType requestType,
            ServerEventType sendMessageStartedEventType,
            ServerEventType sendMessageCompleteEventType)
        {
            _eventLog.Add(new ServerEvent
            {
                EventType = sendMessageStartedEventType,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                LocalIpAddress = MyLocalIpAddress,
                LocalPortNumber = MyServerPortNumber
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            var connectResult = await ConnectToServerAsync().ConfigureAwait(false);
            if (connectResult.Failure)
            {
                return connectResult;
            }

            var socket = connectResult.Value;
            var messageData =
                ServerRequestDataBuilder.ConstructBasicRequest(
                    requestType,
                    MyLocalIpAddress.ToString(),
                    MyServerPortNumber);

            var sendMessageDataResult = await SendMessageData(socket, messageData).ConfigureAwait(false);
            if (sendMessageDataResult.Failure)
            {
                return sendMessageDataResult;
            }

            CloseSocket(socket);

            _eventLog.Add(new ServerEvent
            {
                EventType = sendMessageCompleteEventType,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                LocalIpAddress = MyLocalIpAddress,
                LocalPortNumber = MyServerPortNumber
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            return Result.Ok();
        }

        async Task<Result<Socket>> ConnectToServerAsync()
        {
            var socket =
                new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = ServerEventType.ConnectToRemoteServerStarted,
                    RemoteServerIpAddress = RemoteServerSessionIpAddress,
                    RemoteServerPortNumber = RemoteServerPortNumber
                });

            var connectResult =
                await socket.ConnectWithTimeoutAsync(
                    RemoteServerSessionIpAddress,
                    RemoteServerPortNumber,
                    SocketTimeoutInMilliseconds).ConfigureAwait(false);

            if (connectResult.Failure)
            {
                return Result.Fail<Socket>(connectResult.Error);
            }

            EventOccurred?.Invoke(this,
                new ServerEvent
                    {EventType = ServerEventType.ConnectToRemoteServerComplete});

            return Result.Ok(socket);
        }

        async Task<Result> SendMessageData(Socket socket, byte[] messageData)
        {
            var messageLength = BitConverter.GetBytes(messageData.Length);

            var sendMessageLengthResult =
                await socket.SendWithTimeoutAsync(
                    messageLength,
                    0,
                    messageLength.Length,
                    SocketFlags.None,
                    SocketTimeoutInMilliseconds).ConfigureAwait(false);

            if (sendMessageLengthResult.Failure)
            {
                return sendMessageLengthResult;
            }

            var sendMessageResult =
                await socket.SendWithTimeoutAsync(
                    messageData,
                    0,
                    messageData.Length,
                    SocketFlags.None,
                    SocketTimeoutInMilliseconds).ConfigureAwait(false);

            return sendMessageResult.Success
                ? Result.Ok()
                : sendMessageResult;
        }

        void CloseSocket(Socket socket)
        {
            socket.Shutdown(SocketShutdown.Both);
            socket.Close();
        }

        public async Task<Result> RequestFileListAsync(
            IPAddress remoteServerIpAddress,
            int remoteServerPort,
            string targetFolder)
        {
            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);
            RemoteServerTransferFolderPath = targetFolder;

            EventOccurred?.Invoke(this,
                new ServerEvent
                {
                    EventType = ServerEventType.RequestFileListStarted,
                    LocalIpAddress = MyLocalIpAddress,
                    LocalPortNumber = MyServerPortNumber,
                    RemoteServerIpAddress = RemoteServerSessionIpAddress,
                    RemoteServerPortNumber = RemoteServerPortNumber,
                    RemoteFolder = RemoteServerTransferFolderPath
                });

            var connectResult = await ConnectToServerAsync().ConfigureAwait(false);
            if (connectResult.Failure)
            {
                return connectResult;
            }

            var messageData =
                ServerRequestDataBuilder.ConstructRequestWithStringValue(
                    ServerRequestType.FileListRequest,
                    MyLocalIpAddress.ToString(),
                    MyServerPortNumber,
                    RemoteServerTransferFolderPath);

            var socket = connectResult.Value;
            var sendMessageDataResult = await SendMessageData(socket, messageData).ConfigureAwait(false);
            if (sendMessageDataResult.Failure)
            {
                return sendMessageDataResult;
            }

            CloseSocket(socket);

            EventOccurred?.Invoke(this,
            new ServerEvent {EventType = ServerEventType.RequestFileListComplete});

            return Result.Ok();
        }

        async Task<Result> SendFileListAsync(ServerRequestController requestController)
        {
            var getLocalFolderPath = requestController.GetLocalFolderPath();
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
                RemoteFolder = MyTransferFolderPath,
                RequestId = requestController.RequestId
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            if (!Directory.Exists(MyTransferFolderPath))
            {
                return
                    await SendSimpleMessageToClientAsync(
                        ServerRequestType.RequestedFolderDoesNotExist,
                        ServerEventType.SendNotificationFolderDoesNotExistStarted,
                        ServerEventType.SendNotificationFolderDoesNotExistComplete).ConfigureAwait(false);
            }

            var fileInfoList = new FileInfoList(MyTransferFolderPath);
            if (fileInfoList.Count == 0)
            {
                return await SendSimpleMessageToClientAsync(
                    ServerRequestType.NoFilesAvailableForDownload,
                    ServerEventType.SendNotificationNoFilesToDownloadStarted,
                    ServerEventType.SendNotificationNoFilesToDownloadComplete).ConfigureAwait(false);
            }

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.SendFileListStarted,
                LocalIpAddress = MyLocalIpAddress,
                LocalPortNumber = MyServerPortNumber,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                RemoteServerFileList = fileInfoList,
                LocalFolder = MyTransferFolderPath,
                RequestId = requestController.RequestId
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            var connectResult = await ConnectToServerAsync().ConfigureAwait(false);
            if (connectResult.Failure)
            {
                return connectResult;
            }

            var socket = connectResult.Value;
            var responseData =
                ServerRequestDataBuilder.ConstructFileListResponse(
                    fileInfoList,
                    "*",
                    "|",
                    MyLocalIpAddress.ToString(),
                    MyServerPortNumber,
                    MyTransferFolderPath);

            var sendMessageDataResult = await SendMessageData(socket, responseData).ConfigureAwait(false);
            if (sendMessageDataResult.Failure)
            {
                return sendMessageDataResult;
            }

            CloseSocket(socket);

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.SendFileListComplete,
                RequestId = requestController.RequestId
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            return Result.Ok();
        }

        void HandleRequestedFolderDoesNotExist(ServerRequest request)
        {
            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceivedNotificationFolderDoesNotExist,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                RequestId = request.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());
        }

        void HandleNoFilesAvailableForDownload(ServerRequest request)
        {
            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceivedNotificationNoFilesToDownload,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                RequestId = request.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());
        }

        void ReceiveFileList(ServerRequestController requestController)
        {
            var getFileInfoList = requestController.GetRemoteServerFileInfoList();
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
                RequestId = requestController.RequestId
            });

            EventOccurred?.Invoke(this, _eventLog.Last());
        }

        public Task<Result> RequestServerInfoAsync(IPAddress remoteServerIpAddress, int remoteServerPort)
        {
            RemoteServerInfo = new ServerInfo(remoteServerIpAddress, remoteServerPort);

            return
                SendSimpleMessageToClientAsync(
                    ServerRequestType.ServerInfoRequest,
                    ServerEventType.RequestServerInfoStarted,
                    ServerEventType.RequestServerInfoComplete);
        }

        async Task<Result> SendServerInfoAsync(ServerRequest request)
        {
            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceivedServerInfoRequest,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                RequestId = request.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.SendServerInfoStarted,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                LocalFolder = MyTransferFolderPath,
                LocalIpAddress = MyLocalIpAddress,
                LocalPortNumber = MyServerPortNumber,
                PublicIpAddress = MyPublicIpAddress,
                RequestId = request.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            var connectResult = await ConnectToServerAsync().ConfigureAwait(false);
            if (connectResult.Failure)
            {
                return connectResult;
            }

            var socket = connectResult.Value;
            var responseData =
                ServerRequestDataBuilder.ConstructServerInfoResponse(
                    MyLocalIpAddress.ToString(),
                    MyServerPortNumber,
                    MyPublicIpAddress.ToString(),
                    MyTransferFolderPath);

            var sendMessageDataResult = await SendMessageData(socket, responseData).ConfigureAwait(false);
            if (sendMessageDataResult.Failure)
            {
                return sendMessageDataResult;
            }

            CloseSocket(socket);

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.SendServerInfoComplete,
                RequestId = request.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());

            return Result.Ok();
        }

        void ReceiveServerInfo(ServerRequest request)
        {
            DetermineRemoteServerSessionIpAddress();

            _eventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceivedServerInfo,
                RemoteServerIpAddress = RemoteServerSessionIpAddress,
                RemoteServerPortNumber = RemoteServerPortNumber,
                RemoteFolder = RemoteServerTransferFolderPath,
                LocalIpAddress = RemoteServerLocalIpAddress,
                PublicIpAddress = RemoteServerPublicIpAddress,
                RequestId = request.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());
        }

        void DetermineRemoteServerSessionIpAddress()
        {
            var checkLocalIp = NetworkUtilities.IpAddressIsInRange(RemoteServerLocalIpAddress, _myLanCidrIp);
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

            var shutdownResult =
                await SendSimpleMessageToClientAsync(
                    ServerRequestType.ShutdownServerCommand,
                    ServerEventType.SendShutdownServerCommandStarted,
                    ServerEventType.SendShutdownServerCommandComplete).ConfigureAwait(false);

            return shutdownResult.Success
                ? Result.Ok()
                : Result.Fail($"Error occurred shutting down the server + {shutdownResult.Error}");
        }

        void HandleShutdownServerCommand(ServerRequest request)
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
                RequestId = request.Id
            });

            EventOccurred?.Invoke(this, _eventLog.Last());
        }

        void ShutdownListenSocket()
        {
            EventOccurred?.Invoke(this,
                new ServerEvent
                { EventType = ServerEventType.ShutdownListenSocketStarted });

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
                new ServerEvent
                    {EventType = ServerEventType.ShutdownListenSocketCompletedWithoutError});
        }
    }
}