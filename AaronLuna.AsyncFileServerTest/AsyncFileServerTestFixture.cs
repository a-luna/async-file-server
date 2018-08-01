namespace AaronLuna.AsyncFileServerTest
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Threading;
    using System.Threading.Tasks;

    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using SixLabors.ImageSharp;

    using Common.Extensions;
    using Common.IO;
    using Common.Logging;
    using Common.Network;
    using Common.Result;

    using AsyncFileServer.Model;
    using AsyncFileServer.Controller;
    using TestClasses;

    [TestClass]
    public class AsyncFileServerTestFixture
    {
        const bool GenerateLogFiles = false;
        const string FileName = "smallFile.jpg";

        CancellationTokenSource _cts;
        SocketSettings _socketSettings;
        ServerSettings _clientSettings;
        ServerSettings _serverSettings;
        AsyncFileServerTest _testServer;
        AsyncFileServer _server;
        AsyncFileServer _client;
        ServerState _serverState;
        ServerState _clientState;
        Task<Result> _runServerTask;
        Task<Result> _runClientTask;
        List<string> _clientLogMessages;
        List<string> _serverLogMessages;
        private bool _usedTestServer;
        string _clientLogFilePath;
        string _serverLogFilePath;

        string _localFolder;
        string _remoteFolder;
        string _testFilesFolder;
        string _emptyFolder;
        string _tempFolder;
        string _localFilePath;
        string _remoteFilePath;
        string _restoreFilePath;
        string _transferFolderPath;
        string _cidrIp;
        IPAddress _localIp;
        IPAddress _remoteServerLocalIp;
        IPAddress _remoteServerPublicIp;
        ServerPlatform _remoteServerPlatform;
        ServerPlatform _thisServerPlatform;
        FileInfoList _fileInfoList1;
        FileInfoList _fileInfoList2;
        FileInfoList _fileInfoList3;
        int _fileListInUse;

        bool _serverReceivedTextMessage;
        bool _serverNoFileTransferPending;
        bool _serverReceivedAllFileBytes;
        bool _serverReceivedConfirmationMessage;
        bool _serverRejectedFileTransfer;
        bool _serverHasNoFilesAvailableToDownload;
        bool _serverTransferFolderDoesNotExist;
        bool _serverErrorOccurred;
        bool _serverStoppedSendingFileBytes;
        bool _serverReceivedTransferStalledNotification;

        bool _clientReceivedTextMessage;
        bool _clientNoFileTransferPending;
        bool _clientReceivedAllFileBytes;
        bool _clientRejectedFileTransfer;
        bool _clientReceivedConfirmationMessage;
        bool _clientReceivedServerInfo;
        bool _clientReceivedFileInfoList;
        bool _clientErrorOccurred;
        bool _clientReceivedRetryLimitExceededNotification;

        [TestInitialize]
        public void Setup()
        {
            _transferFolderPath = string.Empty;
            _remoteServerLocalIp = null;
            _remoteServerPublicIp = null;

            _clientLogMessages = new List<string>();
            _serverLogMessages = new List<string>();
            _clientLogFilePath = string.Empty;
            _serverLogFilePath = string.Empty;

            _usedTestServer = false;
            _serverReceivedTextMessage = false;
            _serverNoFileTransferPending = true;
            _serverReceivedAllFileBytes = false;
            _serverReceivedConfirmationMessage = false;
            _serverRejectedFileTransfer = false;
            _serverHasNoFilesAvailableToDownload = false;
            _serverTransferFolderDoesNotExist = false;
            _serverErrorOccurred = false;
            _serverStoppedSendingFileBytes = false;
            _serverReceivedTransferStalledNotification = false;

            _clientReceivedTextMessage = false;
            _clientNoFileTransferPending = true;
            _clientReceivedAllFileBytes = false;
            _clientReceivedConfirmationMessage = false;
            _clientRejectedFileTransfer = false;
            _clientReceivedServerInfo = false;
            _clientReceivedAllFileBytes = false;
            _clientReceivedFileInfoList = false;
            _clientErrorOccurred = false;
            _clientReceivedRetryLimitExceededNotification = false;

            _fileInfoList1 = new FileInfoList();
            _fileInfoList2 = new FileInfoList();
            _fileInfoList3 = new FileInfoList();

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
                ListenBacklogSize = 1,
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

            _server = new AsyncFileServer("Server", _serverSettings);
            _testServer = new AsyncFileServerTest("Test Server", _serverSettings);
            _client = new AsyncFileServer("Client", _clientSettings);

            _serverState = new ServerState(_server);
            _clientState = new ServerState(_client);
        }

        [TestCleanup]
        public async Task ShutdownServerAndClient()
        {
            try
            {
                var runClientResult = Result.Fail("Timeout");
                var runServerResult = Result.Fail("Timeout");

                await _client.ShutdownAsync().ConfigureAwait(false);
                if (_runClientTask == await Task.WhenAny(_runClientTask, Task.Delay(1000)).ConfigureAwait(false))
                {
                    runClientResult = await _runClientTask;
                }

                if (_usedTestServer)
                {
                    await _testServer.ShutdownAsync().ConfigureAwait(false);
                    if (_runServerTask == await Task.WhenAny(_runServerTask, Task.Delay(1000)).ConfigureAwait(false))
                    {
                        runServerResult = await _runServerTask;
                    }
                }
                else
                {
                    await _server.ShutdownAsync().ConfigureAwait(false);
                    if (_runServerTask == await Task.WhenAny(_runServerTask, Task.Delay(1000)).ConfigureAwait(false))
                    {
                        runServerResult = await _runServerTask;
                    }
                }

                var combinedResult = Result.Combine(runClientResult, runServerResult);
                if (combinedResult.Failure)
                {
                    _cts.Cancel();
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

            if (GenerateLogFiles)
            {
                File.AppendAllLines(_clientLogFilePath, _clientLogMessages);
                File.AppendAllLines(_serverLogFilePath, _serverLogMessages);
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
        }

        [TestMethod]
        public async Task VerifySendFile()
        {
            _clientLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifySendFile_client.log";
            _serverLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifySendFile_server.log";

            _serverSettings.LocalServerPortNumber = 8003;
            _clientSettings.LocalServerPortNumber = 8004;

            var sendFilePath = _localFilePath;
            var receiveFilePath = _remoteFilePath;
            var receiveFolderPath = _remoteFolder;

            await _server.InitializeAsync(_serverSettings).ConfigureAwait(false);
            _server.EventOccurred += HandleServerEvent;
            _server.SocketEventOccurred += HandleServerEvent;

            await _client.InitializeAsync(_clientSettings).ConfigureAwait(false);
            _client.EventOccurred += HandleClientEvent;
            _client.SocketEventOccurred += HandleClientEvent;

            var token = _cts.Token;

           _runServerTask =
                Task.Run(() =>
                        _server.RunAsync(token), token);

           _runClientTask =
                Task.Run(() =>
                        _client.RunAsync(token), token);

            while (!_server.IsListening) { }
            while (!_client.IsListening) { }
            
            var sizeOfFileToSend = new FileInfo(sendFilePath).Length;
            FileHelper.DeleteFileIfAlreadyExists(receiveFilePath, 3);
            Assert.IsFalse(File.Exists(receiveFilePath));

            var sendFileResult =
                await _client.SendFileAsync(
                    _localIp,
                    _serverSettings.LocalServerPortNumber,
                    _server.MyInfo.Name,
                    FileName,
                    sizeOfFileToSend,
                    _localFolder,
                    receiveFolderPath).ConfigureAwait(false);

            if (sendFileResult.Failure)
            {
                Assert.Fail("There was an error sending the file to the remote server: " + sendFileResult.Error);
            }

            while (_serverNoFileTransferPending) { }

            var pendingFileTransferId = _serverState.PendingFileTransferIds[0];
            var pendingFileTransfer = _server.GetFileTransferById(pendingFileTransferId).Value;

            var transferResult = await _server.AcceptInboundFileTransferAsync(pendingFileTransfer);
            if (transferResult.Failure)
            {
                Assert.Fail("There was an error receiving the file from the remote server: " + transferResult.Error);
            }

            while (!_serverReceivedAllFileBytes)
            {
                if (_serverErrorOccurred)
                {
                    Assert.Fail("File transfer failed");
                }
            }

            while (!_clientReceivedConfirmationMessage) { }

            Assert.IsTrue(File.Exists(receiveFilePath));
            Assert.AreEqual(FileName, Path.GetFileName(receiveFilePath));
            
            var receivedFileSize = new FileInfo(receiveFilePath).Length;
            Assert.AreEqual(sizeOfFileToSend, receivedFileSize);

            var receiveImageHeight = 0;
            var receiveImageWidth = 0;

            try
            {
                using (var receiveImage = Image.Load(receiveFilePath))
                {
                    receiveImageHeight = receiveImage.Height;
                    receiveImageWidth = receiveImage.Width;
                }
            }
            catch (NotSupportedException ex)
            {
                var error =
                    "An exception was thrown when attempting to load the image file " +
                    $"which was trasferred to the remote server: {Environment.NewLine}" +
                    ex.GetReport();

                Assert.Fail(error);
            }

            using (var sentImage = Image.Load(sendFilePath))
            {
                Assert.AreEqual(sentImage.Height, receiveImageHeight);
                Assert.AreEqual(sentImage.Width, receiveImageWidth);
            }
        }

        [TestMethod]
        public async Task VerifyGetFile()
        {
            _clientLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyGetFile_client.log";
            _serverLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyGetFile_server.log";

            _clientSettings.LocalServerPortNumber = 8005;
            _serverSettings.LocalServerPortNumber = 8006;

            var getFilePath = _remoteFilePath;
            var sentFileSize = new FileInfo(getFilePath).Length;
            var receivedFilePath = _localFilePath;

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

            FileHelper.DeleteFileIfAlreadyExists(receivedFilePath, 3);
            Assert.IsFalse(File.Exists(receivedFilePath));

            var getFileResult =
                await _client.GetFileAsync(
                    _localIp,
                    _serverSettings.LocalServerPortNumber,
                    _server.MyInfo.Name,
                    FileName,
                    sentFileSize,
                    _remoteFolder,
                    _localFolder).ConfigureAwait(false);

            if (getFileResult.Failure)
            {
                var getFileError = "There was an error requesting the file from the remote server: " + getFileResult.Error;
                Assert.Fail(getFileError);
            }

            while (_clientNoFileTransferPending) { }

            var pendingFileTransferId = _clientState.PendingFileTransferIds[0];
            var pendingFileTransfer = _client.GetFileTransferById(pendingFileTransferId).Value;

            var transferResult = await _client.AcceptInboundFileTransferAsync(pendingFileTransfer);
            if (transferResult.Failure)
            {
                Assert.Fail("There was an error receiving the file from the remote server: " + transferResult.Error);
            }

            while (!_clientReceivedAllFileBytes)
            {
                if (_clientErrorOccurred)
                {
                    Assert.Fail("File transfer failed");
                }
            }

            while (!_serverReceivedConfirmationMessage) { }

            Assert.IsTrue(File.Exists(receivedFilePath));
            Assert.AreEqual(FileName, Path.GetFileName(receivedFilePath));
            
            var receivedFileSize = new FileInfo(receivedFilePath).Length;
            Assert.AreEqual(sentFileSize, receivedFileSize);
        }

        [TestMethod]
        public async Task VerifyGetFileAfterStalledFileTransfer()
        {
            _clientLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyGetFileAfterStalledFileTransfer_client.log";
            _serverLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyGetFileAfterStalledFileTransfer_server.log";

            _clientSettings.LocalServerPortNumber = 8007;
            _serverSettings.LocalServerPortNumber = 8008;

            var getFilePath = _remoteFilePath;
            var sentFileSize = new FileInfo(getFilePath).Length;
            var receivedFilePath = _localFilePath;
            _usedTestServer = true;

            await _testServer.InitializeAsync(_serverSettings).ConfigureAwait(false);
            _testServer.EventOccurred += HandleServerEvent;
            _testServer.SocketEventOccurred += HandleServerEvent;
            _testServer.TestEventOccurred += HandleServerEvent;
            _testServer.TestSocketEventOccurred += HandleServerEvent;

            await _client.InitializeAsync(_clientSettings).ConfigureAwait(false);
            _client.EventOccurred += HandleClientEvent;
            _client.SocketEventOccurred += HandleClientEvent;

            var token = _cts.Token;

            _runServerTask =
                Task.Run(() =>
                        _testServer.RunAsync(token),
                    token);

            _runClientTask =
                Task.Run(() =>
                        _client.RunAsync(token),
                    token);

            while (!_testServer.IsListening) { }
            while (!_client.IsListening) { }

            FileHelper.DeleteFileIfAlreadyExists(receivedFilePath, 3);
            Assert.IsFalse(File.Exists(receivedFilePath));

            // 1st transfer attempt
            var getFileResult =
                await _client.GetFileAsync(
                    _localIp,
                    _serverSettings.LocalServerPortNumber,
                    _testServer.MyInfo.Name,
                    FileName,
                    sentFileSize,
                    _remoteFolder,
                    _localFolder).ConfigureAwait(false);

            if (getFileResult.Failure)
            {
                var getFileError = "There was an error requesting the file from the remote server: " + getFileResult.Error;
                Assert.Fail(getFileError);
            }

            // Wait for 1st transfer attempt
            while (_clientNoFileTransferPending) { }

            var changeController = _testServer.ChangeRegularControllerToStalledController(5);
            if (changeController.Failure)
            {
                Assert.Fail(changeController.Error);
            }

            var pendingId = _clientState.PendingFileTransferIds[0];
            var pendingFiletransfer = _client.GetFileTransferById(pendingId).Value;

            var transferResult = await _client.AcceptInboundFileTransferAsync(pendingFiletransfer);
            if (transferResult.Failure)
            {
                Assert.Fail("There was an error receiving the file from the remote server: " + transferResult.Error);
            }

            // Wait for 1st transfer attempt stalled notification 
            while (!_serverStoppedSendingFileBytes)
            {
                if (_clientErrorOccurred)
                {
                    Assert.Fail("File transfer failed");
                }
            }

            var stalledId = 0;
            while (stalledId == 0)
            {
                stalledId = _clientState.StalledFileTransferIds[0];
            }
            
            // Notify server that 1st transfer attempt is stalled
            var notifyTransferStalled =
                await _client.SendNotificationFileTransferStalledAsync(stalledId).ConfigureAwait(false);

            if (notifyTransferStalled.Failure)
            {
                Assert.Fail("There was an error notifying the remote server that the file transfer has stalled: " + transferResult.Error);
            }
            
            // Wait for 1st transfer attempt cancelled notification
            while (!_serverReceivedTransferStalledNotification) { }
            var changeControllerBack = _testServer.ChangeStalledControllerBackToRegularController(5);
            if (changeControllerBack.Failure)
            {
                Assert.Fail(changeControllerBack.Error);
            }

            _clientNoFileTransferPending = true;

            // 2nd transfer attempt
            var retryFileTransfer = await 
                _client.RetryFileTransferAsync(stalledId).ConfigureAwait(false);

            if (retryFileTransfer.Failure)
            {
                Assert.Fail("There was an error attempting to retry the stalled file transfer: " + transferResult.Error);
            }

            // Wait for 2nd transfer attempt
            while (_clientNoFileTransferPending) { }
            
            var pendingId2 = _clientState.PendingFileTransferIds[0];
            var pendingFiletransfer2 = _client.GetFileTransferById(pendingId2).Value;

            var transferResult2 = await _client.AcceptInboundFileTransferAsync(pendingFiletransfer2);
            if (transferResult2.Failure)
            {
                Assert.Fail("There was an error receiving the file from the remote server: " + transferResult2.Error);
            }

            // Wait for all file bytes to be received
            while (!_clientReceivedAllFileBytes)
            {
                if (_clientErrorOccurred)
                {
                    Assert.Fail("File transfer failed");
                }
            }

            // Wait for server to receive confirmation message
            while (!_serverReceivedConfirmationMessage) { }

            Assert.IsTrue(File.Exists(receivedFilePath));
            Assert.AreEqual(FileName, Path.GetFileName(receivedFilePath));

            var receivedFileSize = new FileInfo(receivedFilePath).Length;
            Assert.AreEqual(sentFileSize, receivedFileSize);
        }

        [TestCategory("minicover")]
        [TestMethod]
        public async Task VerifyGetFileAfterRetryLimitExceeded()
        {
            _clientLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyGetFileAfterRetryLimitExceeded_client.log";
            _serverLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyGetFileAfterRetryLimitExceeded_server.log";

            _clientSettings.LocalServerPortNumber = 8009;
            _serverSettings.LocalServerPortNumber = 8010;

            var getFilePath = _remoteFilePath;
            var sentFileSize = new FileInfo(getFilePath).Length;
            var receivedFilePath = _localFilePath;
            _usedTestServer = true;

            await _testServer.InitializeAsync(_serverSettings).ConfigureAwait(false);
            _testServer.EventOccurred += HandleServerEvent;
            _testServer.SocketEventOccurred += HandleServerEvent;
            _testServer.TestEventOccurred += HandleServerEvent;
            _testServer.TestSocketEventOccurred += HandleServerEvent;

            await _client.InitializeAsync(_clientSettings).ConfigureAwait(false);
            _client.EventOccurred += HandleClientEvent;
            _client.SocketEventOccurred += HandleClientEvent;

            var token = _cts.Token;

            _runServerTask =
                Task.Run(() =>
                        _testServer.RunAsync(token),
                    token);

            _runClientTask =
                Task.Run(() =>
                        _client.RunAsync(token),
                    token);

            while (!_testServer.IsListening) { }
            while (!_client.IsListening) { }

            FileHelper.DeleteFileIfAlreadyExists(receivedFilePath, 3);
            Assert.IsFalse(File.Exists(receivedFilePath));

            // 1st transfer attempt
            var getFileResult =
                await _client.GetFileAsync(
                    _localIp,
                    _serverSettings.LocalServerPortNumber,
                    _testServer.MyInfo.Name,
                    FileName,
                    sentFileSize,
                    _remoteFolder,
                    _localFolder).ConfigureAwait(false);

            if (getFileResult.Failure)
            {
                var getFileError = "There was an error requesting the file from the remote server: " + getFileResult.Error;
                Assert.Fail(getFileError);
            }

            // Wait for 1st transfer attempt
            while (_clientNoFileTransferPending) { }

            var changeController = _testServer.ChangeRegularControllerToStalledController(5);
            if (changeController.Failure)
            {
                Assert.Fail(changeController.Error);
            }

            var pendingId = _clientState.PendingFileTransferIds[0];
            var pendingFiletransfer = _client.GetFileTransferById(pendingId).Value;

            var transferResult = await _client.AcceptInboundFileTransferAsync(pendingFiletransfer);
            if (transferResult.Failure)
            {
                Assert.Fail("There was an error receiving the file from the remote server: " + transferResult.Error);
            }

            // Wait for 1st transfer attempt stalled notification 
            while (!_serverStoppedSendingFileBytes)
            {
                if (_clientErrorOccurred)
                {
                    Assert.Fail("File transfer failed");
                }
            }

            var stalledId = 0;
            while (stalledId == 0)
            {
                stalledId = _clientState.StalledFileTransferIds[0];
            }

            // Notify server that 1st transfer attempt is stalled
            var notifyTransferStalled =
                await _client.SendNotificationFileTransferStalledAsync(stalledId).ConfigureAwait(false);

            if (notifyTransferStalled.Failure)
            {
                Assert.Fail("There was an error notifying the remote server that the file transfer has stalled: " + transferResult.Error);
            }

            // Wait for 1st transfer attempt cancelled notification
            while (!_serverReceivedTransferStalledNotification) { }
            var changeControllerBack = _testServer.ChangeStalledControllerBackToRegularController(5);
            if (changeControllerBack.Failure)
            {
                Assert.Fail(changeControllerBack.Error);
            }

            _clientNoFileTransferPending = true;
            _serverStoppedSendingFileBytes = false;
            _serverReceivedTransferStalledNotification = false;

            /////////////////////////////////////////////////////////////////////////////////////////////
            //
            // 2nd transfer attempt
            var retryFileTransfer = await
                _client.RetryFileTransferAsync(stalledId).ConfigureAwait(false);

            if (retryFileTransfer.Failure)
            {
                Assert.Fail("There was an error attempting to retry the stalled file transfer: " + transferResult.Error);
            }

            // Wait for 2nd transfer attempt
            while (_clientNoFileTransferPending) { }

            var changeController2 = _testServer.ChangeRegularControllerToStalledController(5);
            if (changeController2.Failure)
            {
                Assert.Fail(changeController2.Error);
            }

            var pendingId2 = _clientState.PendingFileTransferIds[0];
            var pendingFiletransfer2 = _client.GetFileTransferById(pendingId2).Value;

            var transferResult2 = await _client.AcceptInboundFileTransferAsync(pendingFiletransfer2);
            if (transferResult2.Failure)
            {
                Assert.Fail("There was an error receiving the file from the remote server: " + transferResult2.Error);
            }

            // Wait for 2nd transfer attempt stalled notification 
            while (!_serverStoppedSendingFileBytes)
            {
                if (_clientErrorOccurred)
                {
                    Assert.Fail("File transfer failed");
                }
            }

            var stalledId2 = 0;
            while (stalledId2 == 0)
            {
                stalledId2 = _clientState.StalledFileTransferIds[0];
            }

            // Notify server that 2nd transfer attempt is stalled
            var notifyTransferStalled2 =
                await _client.SendNotificationFileTransferStalledAsync(stalledId2).ConfigureAwait(false);

            if (notifyTransferStalled2.Failure)
            {
                Assert.Fail("There was an error notifying the remote server that the file transfer has stalled: " + notifyTransferStalled2.Error);
            }

            // Wait for 2nd transfer attempt cancelled notification
            while (!_serverReceivedTransferStalledNotification) { }
            var changeControllerBack2 = _testServer.ChangeStalledControllerBackToRegularController(5);
            if (changeControllerBack2.Failure)
            {
                Assert.Fail(changeControllerBack2.Error);
            }

            _clientNoFileTransferPending = true;
            _serverStoppedSendingFileBytes = false;
            _serverReceivedTransferStalledNotification = false;
            _clientReceivedRetryLimitExceededNotification = false;

            /////////////////////////////////////////////////////////////////////////////////////////////
            //
            // 3rd transfer attempt
            var retryFileTransfer2 = await
                _client.RetryFileTransferAsync(stalledId2).ConfigureAwait(false);

            if (retryFileTransfer2.Failure)
            {
                Assert.Fail("There was an error attempting to retry the stalled file transfer: " + retryFileTransfer2.Error);
            }

            while (!_clientReceivedRetryLimitExceededNotification) { }

            var stalledFileTransfer2 = _client.GetFileTransferById(stalledId2).Value;
            Assert.IsFalse(stalledFileTransfer2.RetryLockoutExpired);

            _clientReceivedRetryLimitExceededNotification = false;

            /////////////////////////////////////////////////////////////////////////////////////////////
            //
            // 4th transfer attempt
            var retryFileTransfer3 = await
                _client.RetryFileTransferAsync(stalledId2).ConfigureAwait(false);

            if (retryFileTransfer3.Failure)
            {
                Assert.Fail("There was an error attempting to retry the stalled file transfer: " + retryFileTransfer3.Error);
            }
            
            await Task.Delay(_serverSettings.RetryLimitLockout + TimeSpan.FromSeconds(1));
            Assert.IsTrue(stalledFileTransfer2.RetryLockoutExpired);

            /////////////////////////////////////////////////////////////////////////////////////////////
            //
            // 5th transfer attempt
            var retryFileTransfer4 = await
                _client.RetryFileTransferAsync(stalledId2).ConfigureAwait(false);

            if (retryFileTransfer4.Failure)
            {
                Assert.Fail("There was an error attempting to retry the stalled file transfer: " + retryFileTransfer4.Error);
            }

            // Wait for 5th transfer attempt
            while (_clientNoFileTransferPending) { }

            var pendingId3 = _clientState.PendingFileTransferIds[0];
            var pendingFiletransfer3 = _client.GetFileTransferById(pendingId3).Value;

            var transferResult3 = await _client.AcceptInboundFileTransferAsync(pendingFiletransfer3);
            if (transferResult3.Failure)
            {
                Assert.Fail("There was an error receiving the file from the remote server: " + transferResult3.Error);
            }

            // Wait for all file bytes to be received
            while (!_clientReceivedAllFileBytes)
            {
                if (_clientErrorOccurred)
                {
                    Assert.Fail("File transfer failed");
                }
            }

            // Wait for server to receive confirmation message
            while (!_serverReceivedConfirmationMessage) { }

            Assert.IsTrue(File.Exists(receivedFilePath));
            Assert.AreEqual(FileName, Path.GetFileName(receivedFilePath));

            var receivedFileSize = new FileInfo(receivedFilePath).Length;
            Assert.AreEqual(sentFileSize, receivedFileSize);
        }

        [TestMethod]
        public async Task VerifyRequestServerInfo()
        {
            _clientLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyRequestServerInfo_client.log";
            _serverLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyRequestServerInfo_server.log";
            
            _clientSettings.LocalServerPortNumber = 8011;
            _serverSettings.LocalServerPortNumber = 8012;

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

            Assert.AreEqual(string.Empty, _transferFolderPath);
            Assert.IsNull(_remoteServerPublicIp);
            Assert.IsNull(_remoteServerLocalIp);

            var serverInfoRequest =
                await _client.RequestServerInfoAsync(
                    _localIp,
                    _serverSettings.LocalServerPortNumber)
                    .ConfigureAwait(false);

            if (serverInfoRequest.Failure)
            { 
                Assert.Fail("Error sending request for server connection info.");
            }

            while (!_clientReceivedServerInfo) { }
            
            Assert.AreEqual(_remoteFolder, _transferFolderPath);
            Assert.AreEqual(_thisServerPlatform, _remoteServerPlatform);

            var localIpExpected = _server.MyInfo.LocalIpAddress;

            Assert.IsNotNull(_remoteServerLocalIp);
            Assert.AreEqual(localIpExpected.ToString(), _remoteServerLocalIp.ToString());
            Assert.IsTrue(_remoteServerLocalIp.IsEqualTo(localIpExpected));

            var publicIpExpected = _server.MyInfo.PublicIpAddress;

            Assert.IsNotNull(_remoteServerPublicIp);
            Assert.AreEqual(publicIpExpected.ToString(), _remoteServerPublicIp.ToString());
            Assert.IsTrue(_remoteServerPublicIp.IsEqualTo(publicIpExpected));
            
        }

        [TestMethod]
        public async Task VerifyRequestFileList()
        {
            _clientLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyRequestFileList_client.log";
            _serverLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyRequestFileList_server.log";

            _clientSettings.LocalServerPortNumber = 8013;
            _serverSettings.LocalServerPortNumber = 8014;
            _fileListInUse = 1;

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

            var fileListRequest =
                await _client.RequestFileListAsync(
                        _localIp,
                        _serverSettings.LocalServerPortNumber,
                        _testFilesFolder)
                    .ConfigureAwait(false);

            if (fileListRequest.Failure)
            {
                Assert.Fail("Error sending request for transfer folder path.");
            }

            while (!_clientReceivedFileInfoList) { }

            Assert.AreEqual(4, _fileInfoList1.Count);

            var fiDictionaryActual = new Dictionary<string, long>();
            foreach (var (fileName, folderPath, fileSizeBytes) in _fileInfoList1)
            {
                var filePath = Path.Combine(folderPath, fileName);
                fiDictionaryActual.Add(filePath, fileSizeBytes);
            }

            var expectedFileNames = new List<string>
            {
                Path.Combine(_testFilesFolder, "fake.exe"),
                Path.Combine(_testFilesFolder, "loremipsum1.txt"),
                Path.Combine(_testFilesFolder, "loremipsum2.txt"),
                Path.Combine(_testFilesFolder, "smallFile.jpg")
            };

            foreach (var fileName in expectedFileNames)
            {
                if (fiDictionaryActual.ContainsKey(fileName))
                {
                    var fileSizeExpected = fiDictionaryActual[fileName];
                    var fileSizeActual = new FileInfo(fileName).Length;
                    Assert.AreEqual(fileSizeExpected, fileSizeActual);
                }
                else
                {
                    Assert.Fail($"{fileName} was not found in the list of files.");
                }
            }
        }

        [TestMethod]
        public async Task VerifyNoFilesAvailableToDownload()
        {
            _clientLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyNoFilesAvailableToDownload_client.log";
            _serverLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyNoFilesAvailableToDownload_server.log";

            _clientSettings.LocalServerPortNumber = 8019;
            _serverSettings.LocalServerPortNumber = 8020;
            _fileListInUse = 2;

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

            var fileListRequest1 =
                await _client.RequestFileListAsync(
                    _localIp,
                    _serverSettings.LocalServerPortNumber,
                    _emptyFolder).ConfigureAwait(false);

            if (fileListRequest1.Failure)
            {
                Assert.Fail("Error sending request for transfer folder path.");
            }

            while (!_serverHasNoFilesAvailableToDownload) { }
            
            Assert.AreEqual(0, _fileInfoList2.Count);

            var fileListRequest2 =
                await _client.RequestFileListAsync(
                        _localIp,
                        _serverSettings.LocalServerPortNumber,
                        _testFilesFolder)
                    .ConfigureAwait(false);

            if (fileListRequest2.Failure)
            {
                Assert.Fail("Error sending request for transfer folder path.");
            }

            while (!_clientReceivedFileInfoList) { }

            Assert.AreEqual(4, _fileInfoList2.Count);

            var fiDictionaryActual = new Dictionary<string, long>();
            foreach (var (fileName, folderPath, fileSizeBytes) in _fileInfoList2)
            {
                var filePath = Path.Combine(folderPath, fileName);
                fiDictionaryActual.Add(filePath, fileSizeBytes);
            }

            var expectedFileNames = new List<string>
            {
                Path.Combine(_testFilesFolder, "fake.exe"),
                Path.Combine(_testFilesFolder, "loremipsum1.txt"),
                Path.Combine(_testFilesFolder, "loremipsum2.txt"),
                Path.Combine(_testFilesFolder, "smallFile.jpg")
            };

            foreach (var fileName in expectedFileNames)
            {
                if (fiDictionaryActual.ContainsKey(fileName))
                {
                    var fileSizeExpected = fiDictionaryActual[fileName];
                    var fileSizeActual = new FileInfo(fileName).Length;
                    Assert.AreEqual(fileSizeExpected, fileSizeActual);
                }
                else
                {
                    Assert.Fail($"{fileName} was not found in the list of files.");
                }
            }
        }

        [TestMethod]
        public async Task VerifyRequestedFolderDoesNotExist()
        {
            _clientLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyRequestedFolderDoesNotExist_client.log";
            _serverLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyRequestedFolderDoesNotExist_server.log";

            _clientSettings.LocalServerPortNumber = 8021;
            _serverSettings.LocalServerPortNumber = 8022;
            _fileListInUse = 3;

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

            Assert.IsFalse(Directory.Exists(_tempFolder));

            var fileListRequest1 =
                await _client.RequestFileListAsync(
                        _localIp,
                        _serverSettings.LocalServerPortNumber,
                        _tempFolder)
                    .ConfigureAwait(false);

            if (fileListRequest1.Failure)
            {
                Assert.Fail("Error sending request for transfer folder path.");
            }

            while (!_serverTransferFolderDoesNotExist) { }
            
            Assert.AreEqual(0, _fileInfoList3.Count);

            var fileListRequest2 =
                await _client.RequestFileListAsync(
                        _localIp,
                        _serverSettings.LocalServerPortNumber,
                        _testFilesFolder)
                    .ConfigureAwait(false);

            if (fileListRequest2.Failure)
            {
                Assert.Fail("Error sending request for transfer folder path.");
            }

            while (!_clientReceivedFileInfoList) { }

            Assert.AreEqual(4, _fileInfoList3.Count);

            var fiDictionaryActual = new Dictionary<string, long>();
            foreach (var (fileName, folderPath, fileSizeBytes) in _fileInfoList3)
            {
                var filePath = Path.Combine(folderPath, fileName);
                fiDictionaryActual.Add(filePath, fileSizeBytes);
            }

            var expectedFileNames = new List<string>
            {
                Path.Combine(_testFilesFolder, "fake.exe"),
                Path.Combine(_testFilesFolder, "loremipsum1.txt"),
                Path.Combine(_testFilesFolder, "loremipsum2.txt"),
                Path.Combine(_testFilesFolder, "smallFile.jpg")
            };

            foreach (var fileName in expectedFileNames)
            {
                if (fiDictionaryActual.ContainsKey(fileName))
                {
                    var fileSizeExpected = fiDictionaryActual[fileName];
                    var fileSizeActual = new FileInfo(fileName).Length;
                    Assert.AreEqual(fileSizeExpected, fileSizeActual);
                }
                else
                {
                    Assert.Fail($"{fileName} was not found in the list of files.");
                }
            }
        }

        [TestMethod]
        public async Task VerifyOutboundFileTransferRejected()
        {
            _clientLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyOutboundFileTransferRejected_client.log";
            _serverLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyOutboundFileTransferRejected_server.log";

            _serverSettings.LocalServerPortNumber = 8015;
            _clientSettings.LocalServerPortNumber = 8016;

            await _server.InitializeAsync(_serverSettings).ConfigureAwait(false);
            _server.EventOccurred += HandleServerEvent;
            _server.SocketEventOccurred += HandleServerEvent;

            await _client.InitializeAsync(_clientSettings).ConfigureAwait(false);
            _client.EventOccurred += HandleClientEvent;
            _client.SocketEventOccurred += HandleClientEvent;

            var token = _cts.Token;
            var sendFilePath = _localFilePath;
            var receiveFilePath = _remoteFilePath;

            _runServerTask =
                Task.Run(() =>
                    _server.RunAsync(token), token);

            _runClientTask =
                Task.Run(() =>
                    _client.RunAsync(token), token);

            while (!_server.IsListening) { }
            while (!_client.IsListening) { }

            var sizeOfFileToSend = new FileInfo(sendFilePath).Length;
            Assert.IsTrue(File.Exists(receiveFilePath));

            var sendFileResult =
                await _client.SendFileAsync(
                    _localIp,
                    _serverSettings.LocalServerPortNumber,
                    _server.MyInfo.Name,
                    FileName,
                    sizeOfFileToSend,
                    _localFolder,
                    _remoteFolder)
                    .ConfigureAwait(false);

            if (sendFileResult.Failure)
            {
                Assert.Fail("Error occurred sending outbound file request to server");
            }
            
            while (!_serverRejectedFileTransfer) { }
        }

        [TestMethod]
        public async Task VerifyInboundFileTransferRejected()
        {
            _clientLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyInboundFileTransferRejected_client.log";
            _serverLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyInboundFileTransferRejected_server.log";

            _clientSettings.LocalServerPortNumber = 8017;
            _serverSettings.LocalServerPortNumber = 8018;
            var getFilePath = _remoteFilePath;
            var sentFileSize = new FileInfo(getFilePath).Length;
            var receivedFilePath = _localFilePath;

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

            Assert.IsTrue(File.Exists(receivedFilePath));

            var getFileResult =
                await _client.GetFileAsync(
                    _localIp,
                    _serverSettings.LocalServerPortNumber,
                    _server.MyInfo.Name,
                    FileName,
                    sentFileSize,
                    _remoteFolder,
                    _localFolder)
                    .ConfigureAwait(false);

            if (getFileResult.Failure)
            {
                Assert.Fail("There was an error requesting the file from the remote server: " + getFileResult.Error);
            }
            
            while (!_clientRejectedFileTransfer) { }
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
                    _clientNoFileTransferPending = false;
                    break;

                case ServerEventType.ReceivedTextMessage:
                    _clientReceivedTextMessage = true;
                    break;

                case ServerEventType.ReceivedServerInfo:
                    _transferFolderPath = serverEvent.RemoteFolder;
                    _remoteServerPublicIp = serverEvent.PublicIpAddress;
                    _remoteServerLocalIp = serverEvent.LocalIpAddress;
                    _remoteServerPlatform = serverEvent.RemoteServerPlatform;
                    _clientReceivedServerInfo = true;
                    break;

                case ServerEventType.ReceivedFileList:

                    switch (_fileListInUse)
                    {
                        case 1:
                            _fileInfoList1 = serverEvent.RemoteServerFileList;
                            break;

                        case 2:
                            _fileInfoList2 = serverEvent.RemoteServerFileList;
                            break;

                        case 3:
                            _fileInfoList3 = serverEvent.RemoteServerFileList;
                            break;
                    }

                    _clientReceivedFileInfoList = true;
                    break;

                case ServerEventType.ReceiveFileBytesComplete:
                    _clientReceivedAllFileBytes = true;
                    break;

                case ServerEventType.ReceivedRetryLimitExceeded:
                    _clientReceivedRetryLimitExceededNotification = true;
                    break;

                case ServerEventType.RemoteServerRejectedFileTransfer:
                    _serverRejectedFileTransfer = true;
                    break;

                case ServerEventType.ReceivedNotificationNoFilesToDownload:
                    _serverHasNoFilesAvailableToDownload = true;
                    break;

                case ServerEventType.ReceivedNotificationFolderDoesNotExist:
                    _serverTransferFolderDoesNotExist = true;
                    break;

                case ServerEventType.RemoteServerConfirmedFileTransferCompleted:
                    _clientReceivedConfirmationMessage = true;
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
                    _serverNoFileTransferPending = false;
                    break;

                case ServerEventType.ReceivedTextMessage:
                    _serverReceivedTextMessage = true;
                    break;

                case ServerEventType.StoppedSendingFileBytes:
                    _serverStoppedSendingFileBytes = true;
                    break;

                case ServerEventType.FileTransferStalled:
                    _serverReceivedTransferStalledNotification = true;
                    break;

                case ServerEventType.ReceiveFileBytesComplete:
                    _serverReceivedAllFileBytes = true;
                    break;

                case ServerEventType.RemoteServerRejectedFileTransfer:
                    _clientRejectedFileTransfer = true;
                    break;

                case ServerEventType.RemoteServerConfirmedFileTransferCompleted:
                    _serverReceivedConfirmationMessage = true;
                    break;

                case ServerEventType.ErrorOccurred:
                    _serverErrorOccurred = true;
                    break;
            }
        }
    }
}
