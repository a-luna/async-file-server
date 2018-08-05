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

    [TestClass]
    public partial class AsyncFileServerTestFixture
    {
        const string FileName = "smallFile.jpg";

        CancellationTokenSource _cts;
        SocketSettings _socketSettings;
        ServerSettings _clientSettings;
        ServerSettings _serverSettings;
        List<string> _serverLogMessages;
        List<string> _clientLogMessages;
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
        IPAddress _remoteServerLocalIp;
        IPAddress _remoteServerPublicIp;
        ServerPlatform _remoteServerPlatform;
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
            _generateLogFiles = false;
            _clientLogMessages = new List<string>();
            _serverLogMessages = new List<string>();

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

            _cidrIp = "172.20.10.0/28";
            _remoteServerPlatform = ServerPlatform.None;

            var getCidrIp = NetworkUtilities.GetCidrIp();
            if (getCidrIp.Success)
            {
                _cidrIp = getCidrIp.Value;
            }

            var getLocalIp = NetworkUtilities.GetLocalIPv4Address(_cidrIp);
            if (getLocalIp.Success)
            {
            }
            
            _cts = new CancellationTokenSource();

            _socketSettings = new SocketSettings
            {
                ListenBacklogSize = 5,
                BufferSize = 1024,
                SocketTimeoutInMilliseconds = 10000
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

            Assert.IsNull(_remoteServerLocalIp);
            Assert.IsNull(_remoteServerPublicIp);           
            Assert.AreEqual(string.Empty, _remoteServerFolderPath);
            Assert.AreEqual(ServerPlatform.None, _remoteServerPlatform);

            var serverInfoRequest =
                await client.RequestServerInfoAsync(
                        server.MyInfo.LocalIpAddress,
                        server.MyInfo.PortNumber)
                    .ConfigureAwait(false);

            if (serverInfoRequest.Failure)
            {
                Assert.Fail("Error sending request for server connection info.");
            }

            while (!_clientReceivedServerInfo) { }
            
            Assert.AreEqual(server.MyInfo.TransferFolder, _remoteServerFolderPath);
            Assert.AreEqual(server.MyInfo.Platform, _remoteServerPlatform);

            Assert.IsNotNull(_remoteServerLocalIp);
            Assert.AreEqual(server.MyInfo.LocalIpAddress.ToString(), _remoteServerLocalIp.ToString());
            Assert.IsTrue(_remoteServerLocalIp.IsEqualTo(server.MyInfo.LocalIpAddress));
            
            Assert.IsNotNull(_remoteServerPublicIp);
            Assert.AreEqual(server.MyInfo.PublicIpAddress.ToString(), _remoteServerPublicIp.ToString());
            Assert.IsTrue(_remoteServerPublicIp.IsEqualTo(server.MyInfo.PublicIpAddress));

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
            var clientLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifySendTextMessage_client.log";
            var serverLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifySendTextMessage_server.log";

            _clientSettings.LocalServerPortNumber = 8001;
            _serverSettings.LocalServerPortNumber = 8002;

            const string messageForServer = "Hello, fellow TPL $ocket Server! This is a text message with a few special ch@r@cters. `~/|\\~'";
            const string messageForClient = "I don't know who or what you are referring to. I am a normal human, sir, and most definitely NOT some type of server. Good day.";

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

            Assert.IsTrue(server.TextSessions.Count == 0);

            var sendMessageToServer =
                await client.SendTextMessageAsync(
                        messageForServer,
                        server.MyInfo.LocalIpAddress,
                        server.MyInfo.PortNumber)
                    .ConfigureAwait(false);

            if (sendMessageToServer.Failure)
            {
                Assert.Fail($"There was an error sending a text message to the server: {sendMessageToServer.Error}");
            }

            while (!_serverReceivedTextMessage) { }
            
            Assert.IsTrue(server.TextSessions.Count > 0);
            Assert.IsTrue(server.TextSessions[0].MessageCount > 0);
            Assert.AreEqual(messageForServer, server.TextSessions[0].Messages[0].Message);

            var sendMessageToClient =
                await server.SendTextMessageAsync(
                        messageForClient,
                        client.MyInfo.LocalIpAddress,
                        client.MyInfo.PortNumber)
                    .ConfigureAwait(false);

            if (sendMessageToClient.Failure)
            {
                Assert.Fail($"There was an error sending a text message to the client: {sendMessageToClient.Error}");
            }

            while (!_clientReceivedTextMessage) { }

            Assert.IsTrue(client.TextSessions.Count > 0);
            Assert.IsTrue(client.TextSessions[0].MessageCount > 1);
            Assert.AreEqual(messageForClient, client.TextSessions[0].Messages[1].Message);

            await ShutdownServerAsync(client, runClientTask);
            await ShutdownServerAsync(server, runServerTask);

            if (_generateLogFiles)
            {
                File.AppendAllLines(clientLogFilePath, _clientLogMessages);
                File.AppendAllLines(serverLogFilePath, _serverLogMessages);
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