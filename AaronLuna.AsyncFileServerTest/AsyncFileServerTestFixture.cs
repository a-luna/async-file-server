namespace AaronLuna.AsyncFileServerTest
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Threading;
    using System.Threading.Tasks;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    using Common.Extensions;
    using Common.IO;
    using Common.Logging;
    using Common.Network;
    using Common.Result;

    using AsyncFileServer.Model;
    using AsyncFileServer.Controller;
    using TestClasses;

    [TestClass]
    public partial class AsyncFileServerTestFixture
    {
        const string FileName = "smallFile.jpg";

        CancellationTokenSource _cts;
        SocketSettings _socketSettings;
        ServerSettings _clientSettings;
        ServerSettings _serverSettings;
        AsyncFileServer _server;
        AsyncFileServer _client;
        AsyncFileServerTest _testServer;
        ServerState _serverState;
        ServerState _clientState;
        Task<Result> _runServerTask;
        Task<Result> _runClientTask;
        List<string> _serverLogMessages;
        List<string> _clientLogMessages;
        string _serverLogFilePath;
        string _clientLogFilePath;
        bool _generateLogFiles;

        string _localFolder;
        string _remoteFolder;
        string _testFilesFolder;
        string _emptyFolder;
        string _tempFolder;
        string _localFilePath;
        string _remoteFilePath;
        string _restoreFilePath;
        string _remoteServerFolderPath;
        string _cidrIp;
        IPAddress _localIp;
        IPAddress _remoteServerLocalIp;
        IPAddress _remoteServerPublicIp;
        ServerPlatform _remoteServerPlatform;
        ServerPlatform _thisServerPlatform;
        FileInfoList _fileInfoList;

        bool _serverReceivedTextMessage;
        bool _serverFileTransferPending;
        bool _serverReceivedAllFileBytes;
        bool _serverConfirmedFileTransferComplete;
        bool _serverRejectedFileTransfer;
        bool _serverProcessingRequestBacklogStarted;
        bool _serverProcessingRequestBacklogComplete;
        bool _serverErrorOccurred;

        bool _serverSendFileBytesStarted;
        bool _serverSendFileBytesComplete;
        bool _serverStoppedSendingFileBytes;
        bool _serverWasNotifiedFileTransferStalled;

        bool _clientReceivedTextMessage;
        bool _clientFileTransferPending;
        bool _clientReceiveFileBytesStarted;
        bool _clientReceiveFileBytesComplete;
        bool _clientConfirmedFileTransferComplete;
        bool _clientRejectedFileTransfer;
        bool _clientProcessingRequestBacklogStarted;
        bool _clientProcessingRequestBacklogComplete;
        bool _clientErrorOccurred;

        bool _clientReceivedServerInfo;
        bool _clientReceivedFileInfoList;
        bool _clientWasNotifiedFolderIsEmpty;
        bool _clientWasNotifiedFolderDoesNotExist;        
        bool _clientWasNotifiedRetryLimitExceeded;
        bool _clientWasNotifiedFileDoesNotExist;

        [TestInitialize]
        public void Setup()
        {
            _generateLogFiles = true;
            _clientLogMessages = new List<string>();
            _serverLogMessages = new List<string>();
            _clientLogFilePath = string.Empty;
            _serverLogFilePath = string.Empty;

            _remoteServerFolderPath = string.Empty;
            _remoteServerLocalIp = null;
            _remoteServerPublicIp = null;

            _serverReceivedTextMessage = false;
            _serverFileTransferPending = false;
            _serverReceivedAllFileBytes = false;
            _serverConfirmedFileTransferComplete = false;
            _serverRejectedFileTransfer = false;
            _serverProcessingRequestBacklogStarted = false;
            _serverProcessingRequestBacklogComplete = false;
            _serverErrorOccurred = false;

            _serverSendFileBytesStarted = false;
            _serverSendFileBytesComplete = false;
            _serverStoppedSendingFileBytes = false;
            _serverWasNotifiedFileTransferStalled = false;

            _clientReceivedTextMessage = false;
            _clientFileTransferPending = false;
            _clientReceiveFileBytesStarted = false;
            _clientReceiveFileBytesComplete = false;
            _clientConfirmedFileTransferComplete = false;
            _clientRejectedFileTransfer = false;
            _clientProcessingRequestBacklogStarted = false;
            _clientProcessingRequestBacklogComplete = false;
            _clientErrorOccurred = false;
            
            _clientReceivedServerInfo = false;
            _clientReceiveFileBytesComplete = false;
            _clientWasNotifiedFolderIsEmpty = false;
            _clientWasNotifiedFolderDoesNotExist = false;
            _clientWasNotifiedRetryLimitExceeded = false;
            _clientWasNotifiedFileDoesNotExist = false;

            _fileInfoList = new FileInfoList();

            var currentPath = Directory.GetCurrentDirectory();
            var index = currentPath.IndexOf("bin", StringComparison.Ordinal);
            _testFilesFolder = $"{currentPath.Remove(index - 1)}{Path.DirectorySeparatorChar}TestFiles{Path.DirectorySeparatorChar}";

            _localFolder = _testFilesFolder + $"Client{Path.DirectorySeparatorChar}";
            _remoteFolder = _testFilesFolder + $"Server{Path.DirectorySeparatorChar}";
            _emptyFolder = _testFilesFolder + $"EmptyFolder{Path.DirectorySeparatorChar}";
            _tempFolder = _testFilesFolder + $"temp{Path.DirectorySeparatorChar}";

            Directory.CreateDirectory(_localFolder);
            Directory.CreateDirectory(_remoteFolder);
            Directory.CreateDirectory(_emptyFolder);

            _localFilePath = _localFolder + FileName;
            _remoteFilePath = _remoteFolder + FileName;
            _restoreFilePath = _testFilesFolder + FileName;

            FileHelper.DeleteFileIfAlreadyExists(_localFilePath, 3);
            if (File.Exists(_restoreFilePath))
            {
                File.Copy(_restoreFilePath, _localFilePath);
            }

            FileHelper.DeleteFileIfAlreadyExists(_remoteFilePath, 3);
            if (File.Exists(_restoreFilePath))
            {
                File.Copy(_restoreFilePath, _remoteFilePath);
            }

            _localIp = IPAddress.Loopback;
            _remoteServerPlatform = ServerPlatform.None;
            _thisServerPlatform = Environment.OSVersion.Platform.ToServerPlatform();

            //_cidrIp = "192.168.1.0/24";
            _cidrIp = "172.20.10.0/28";

            var getCidrIp = NetworkUtilities.GetCidrIp();
            if (getCidrIp.Success)
            {
                _cidrIp = getCidrIp.Value;
            }

            var getLocalIpResult = NetworkUtilities.GetLocalIPv4Address(_cidrIp);
            if (getLocalIpResult.Success)
            {
                _localIp = getLocalIpResult.Value;
            }
            
            _cts = new CancellationTokenSource();

            _socketSettings = new SocketSettings
            {
                ListenBacklogSize = 5,
                BufferSize = 1024,
                SocketTimeoutInMilliseconds = 5000
            };

            _clientSettings = new ServerSettings
            {
                LocalServerFolderPath = _localFolder,
                LocalNetworkCidrIp = _cidrIp,
                SocketSettings = _socketSettings,
                TransferUpdateInterval = 0.10f,
                FileTransferStalledTimeout = TimeSpan.FromSeconds(5),
                TransferRetryLimit = 2,
                RetryLimitLockout = TimeSpan.FromSeconds(3),
                LogLevel = LogLevel.Info
            };

            _serverSettings = new ServerSettings
            {
                LocalServerFolderPath = _remoteFolder,
                LocalNetworkCidrIp = _cidrIp,
                SocketSettings = _socketSettings,
                TransferUpdateInterval = 0.10f,
                FileTransferStalledTimeout = TimeSpan.FromSeconds(5),
                TransferRetryLimit = 2,
                RetryLimitLockout = TimeSpan.FromSeconds(3),
                LogLevel = LogLevel.Info
            };

            _server = new AsyncFileServer("server", _serverSettings);
            _testServer = new AsyncFileServerTest("test server", _serverSettings);
            _client = new AsyncFileServer("client", _clientSettings);
            
            _serverState = new ServerState(_server);
            _clientState = new ServerState(_client);
        }

        [TestMethod]
        public async Task VerifyRequestServerInfo()
        {
            var clientLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyRequestServerInfo_client.log";
            var serverLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyRequestServerInfo_server.log";
            
            _clientSettings.LocalServerPortNumber = 8011;
            _serverSettings.LocalServerPortNumber = 8012;

            var token = _cts.Token;

            var server = new AsyncFileServer("server", _serverSettings);
            server.EventOccurred += HandleServerEvent;
            server.SocketEventOccurred += HandleServerEvent;

            var client = new AsyncFileServer("client", _clientSettings);
            client.EventOccurred += HandleClientEvent;
            client.SocketEventOccurred += HandleClientEvent;

            await server.InitializeAsync(_serverSettings).ConfigureAwait(false);
            await client.InitializeAsync(_clientSettings).ConfigureAwait(false);

            var runServerTask = Task.Run(() => server.RunAsync(token), token);
            var runClientTask = Task.Run(() => client.RunAsync(token), token);

            while (!server.IsListening) { }
            while (!client.IsListening) { }

            Assert.AreEqual(string.Empty, _remoteServerFolderPath);
            Assert.IsNull(_remoteServerPublicIp);
            Assert.IsNull(_remoteServerLocalIp);

            var serverInfoRequest =
                await client.RequestServerInfoAsync(
                        _localIp,
                        _serverSettings.LocalServerPortNumber)
                    .ConfigureAwait(false);

            if (serverInfoRequest.Failure)
            {
                Assert.Fail("Error sending request for server connection info.");
            }

            while (!_clientReceivedServerInfo) { }
            
            Assert.AreEqual(_remoteFolder, _remoteServerFolderPath);
            Assert.AreEqual(_thisServerPlatform, _remoteServerPlatform);

            var localIpExpected = server.MyInfo.LocalIpAddress;

            Assert.IsNotNull(_remoteServerLocalIp);
            Assert.AreEqual(localIpExpected.ToString(), _remoteServerLocalIp.ToString());
            Assert.IsTrue(_remoteServerLocalIp.IsEqualTo(localIpExpected));

            var publicIpExpected = server.MyInfo.PublicIpAddress;

            Assert.IsNotNull(_remoteServerPublicIp);
            Assert.AreEqual(publicIpExpected.ToString(), _remoteServerPublicIp.ToString());
            Assert.IsTrue(_remoteServerPublicIp.IsEqualTo(publicIpExpected));

            await ShutdownServerAsync(client, runClientTask);
            await ShutdownServerAsync(server, runServerTask);

            if (_generateLogFiles)
            {
                File.AppendAllLines(clientLogFilePath, _clientLogMessages);
                File.AppendAllLines(serverLogFilePath, _serverLogMessages);
            }
        }

        [TestMethod]
        public async Task VerifySendTextMessage()
        {
            _clientLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifySendTextMessage_client.log";
            _serverLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifySendTextMessage_server.log";

            _clientSettings.LocalServerPortNumber = 8001;
            _serverSettings.LocalServerPortNumber = 8002;

            const string sentToServer = "Hello, fellow TPL $ocket Server! This is a text message with a few special ch@r@cters. `~/|\\~'";
            const string sentToClient = "I don't know who or what you are referring to. I am a normal human, sir, and most definitely NOT some type of server. Good day.";

            await _server.InitializeAsync(_serverSettings).ConfigureAwait(false);
            _server.EventOccurred += HandleServerEvent;
            _server.SocketEventOccurred += HandleServerEvent;

            await _client.InitializeAsync(_clientSettings).ConfigureAwait(false);
            _client.EventOccurred += HandleClientEvent;
            _client.SocketEventOccurred += HandleClientEvent;

            var token = _cts.Token;

           _runServerTask =
                Task.Run(() =>
                    _server.RunAsync(token),
                    token);

           _runClientTask =
                Task.Run(() =>
                    _client.RunAsync(token),
                    token);

            while (!_server.IsListening) { }
            while (!_client.IsListening) { }

            var sendMessageResult1 =
                await _client.SendTextMessageAsync(
                        sentToServer,
                        _localIp,
                        _serverSettings.LocalServerPortNumber)
                    .ConfigureAwait(false);

            if (sendMessageResult1.Failure)
            {
                Assert.Fail($"There was an error sending a text message to the server: {sendMessageResult1.Error}");
            }

            while (!_serverReceivedTextMessage) { }

            var receivedByServer = string.Empty;
            if (_server.TextSessions.Count > 0)
            {
                if (_server.TextSessions[0].MessageCount > 0)
                {
                    receivedByServer = _server.TextSessions[0].Messages[0].Message;
                }
            }

            Assert.AreEqual(sentToServer, receivedByServer);

            var sendMessageResult2 =
                await _server.SendTextMessageAsync(
                        sentToClient,
                        _localIp,
                        _clientSettings.LocalServerPortNumber)
                    .ConfigureAwait(false);

            if (sendMessageResult2.Failure)
            {
                Assert.Fail($"There was an error sending a text message to the client: {sendMessageResult2.Error}");
            }

            while (!_clientReceivedTextMessage) { }

            var receivedByClient = string.Empty;
            if (_client.TextSessions.Count > 0)
            {
                if (_client.TextSessions[0].MessageCount > 1)
                {
                    receivedByClient = _client.TextSessions[0].Messages[1].Message;
                }
            }

            Assert.AreEqual(sentToClient, receivedByClient);

            await ShutdownServerAsync(_client, _runClientTask);
            await ShutdownServerAsync(_server, _runServerTask);

            if (_generateLogFiles)
            {
                File.AppendAllLines(_clientLogFilePath, _clientLogMessages);
                File.AppendAllLines(_serverLogFilePath, _serverLogMessages);
            }
        }

        void HandleClientEvent(object sender, ServerEvent serverEvent)
        {
            var logMessageForConsole =
                $"(client)\t{DateTime.Now:MM/dd/yyyy HH:mm:ss.fff}\t{serverEvent}";

            var logMessageForFile =
                $"(client)\t{DateTime.Now:MM/dd/yyyy HH:mm:ss.fff}\t{serverEvent.GetLogFileEntry()}";

            Console.WriteLine(logMessageForConsole);
            _clientLogMessages.Add(logMessageForFile);

            switch (serverEvent.EventType)
            {
                case ServerEventType.PendingFileTransfer:
                    _clientFileTransferPending = true;
                    break;

                case ServerEventType.ReceivedTextMessage:
                    _clientReceivedTextMessage = true;
                    break;

                case ServerEventType.ReceivedServerInfo:
                    _remoteServerFolderPath = serverEvent.RemoteFolder;
                    _remoteServerPublicIp = serverEvent.PublicIpAddress;
                    _remoteServerLocalIp = serverEvent.LocalIpAddress;
                    _remoteServerPlatform = serverEvent.RemoteServerPlatform;
                    _clientReceivedServerInfo = true;
                    break;

                case ServerEventType.ReceivedFileList:
                    _fileInfoList = serverEvent.RemoteServerFileList;
                    _clientReceivedFileInfoList = true;
                    break;

                case ServerEventType.ReceivedNotificationFileDoesNotExist:
                    _clientWasNotifiedFileDoesNotExist = true;
                    break;

                case ServerEventType.ReceiveFileBytesStarted:
                    _clientReceiveFileBytesStarted = true;
                    break;

                case ServerEventType.ReceiveFileBytesComplete:
                    _clientReceiveFileBytesComplete = true;
                    break;

                case ServerEventType.ReceivedRetryLimitExceeded:
                    _clientWasNotifiedRetryLimitExceeded = true;
                    break;

                case ServerEventType.RemoteServerRejectedFileTransfer:
                    _serverRejectedFileTransfer = true;
                    break;

                case ServerEventType.ReceivedNotificationFolderIsEmpty:
                    _clientWasNotifiedFolderIsEmpty = true;
                    break;

                case ServerEventType.ReceivedNotificationFolderDoesNotExist:
                    _clientWasNotifiedFolderDoesNotExist = true;
                    break;

                case ServerEventType.RemoteServerConfirmedFileTransferCompleted:
                    _serverConfirmedFileTransferComplete = true;
                    break;

                case ServerEventType.ProcessRequestBacklogStarted:
                    _clientProcessingRequestBacklogStarted = true;
                    break;

                case ServerEventType.ProcessRequestBacklogComplete:
                    _clientProcessingRequestBacklogComplete = true;
                    break;

                case ServerEventType.ErrorOccurred:
                    _clientErrorOccurred = true;
                    break;
            }
        }

        void HandleServerEvent(object sender, ServerEvent serverEvent)
        {
            var logMessageForConsole =
                $"(server)\t{DateTime.Now:MM/dd/yyyy HH:mm:ss.fff}\t{serverEvent}";

            var logMessageForFile =
                $"(server)\t{DateTime.Now:MM/dd/yyyy HH:mm:ss.fff}\t{serverEvent.GetLogFileEntry()}";

            Console.Write(logMessageForConsole);
            _serverLogMessages.Add(logMessageForFile);

            switch (serverEvent.EventType)
            {
                case ServerEventType.PendingFileTransfer:
                    _serverFileTransferPending = true;
                    break;

                case ServerEventType.ReceivedTextMessage:
                    _serverReceivedTextMessage = true;
                    break;

                case ServerEventType.StoppedSendingFileBytes:
                    _serverStoppedSendingFileBytes = true;
                    break;

                case ServerEventType.FileTransferStalled:
                    _serverWasNotifiedFileTransferStalled = true;
                    break;

                case ServerEventType.ReceiveFileBytesComplete:
                    _serverReceivedAllFileBytes = true;
                    break;

                case ServerEventType.SendFileBytesStarted:
                    _serverSendFileBytesStarted = true;
                    break;

                case ServerEventType.SendFileBytesComplete:
                    _serverSendFileBytesComplete = true;
                    break;

                case ServerEventType.RemoteServerRejectedFileTransfer:
                    _clientRejectedFileTransfer = true;
                    break;

                case ServerEventType.RemoteServerConfirmedFileTransferCompleted:
                    _clientConfirmedFileTransferComplete = true;
                    break;

                case ServerEventType.ProcessRequestBacklogStarted:
                    _serverProcessingRequestBacklogStarted = true;
                    break;

                case ServerEventType.ProcessRequestBacklogComplete:
                    _serverProcessingRequestBacklogComplete = true;
                    break;

                case ServerEventType.ErrorOccurred:
                    _serverErrorOccurred = true;
                    break;
            }
        }

        async Task ShutdownServerAsync(AsyncFileServer server, Task<Result> runServerTask)
        {
            try
            {
                var runClientResult = Result.Fail("Timeout");
                await server.ShutdownAsync().ConfigureAwait(false);

                if (runServerTask == await Task.WhenAny(runServerTask, Task.Delay(1000)).ConfigureAwait(false))
                {
                    runClientResult = await runServerTask;
                }

                if (runClientResult.Failure)
                {
                    //_cts.Cancel();
                }
            }
            catch (AggregateException ex)
            {
                Console.WriteLine("\nException messages:");
                foreach (var ie in ex.InnerExceptions)
                {
                    Console.WriteLine($"\t{ie.GetType().Name}: {ie.Message}");
                }
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine("Accept connection task canceled");
            }
        }
    }
}