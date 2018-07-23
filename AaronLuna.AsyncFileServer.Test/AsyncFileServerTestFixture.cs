namespace AaronLuna.AsyncFileServer.Test
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Threading;
    using System.Threading.Tasks;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    using Common.IO;
    using Common.Logging;
    using Common.Network;
    using Common.Result;

    using Controller;
    using Model;

    [TestClass]
    public class AsyncFileServerTestFixture
    {
        const bool GenerateLogFiles = false;
        const string FileName = "smallFile.jpg";

        CancellationTokenSource _cts;
        SocketSettings _socketSettings;
        AsyncFileServer _server;
        AsyncFileServer _client;
        Task<Result> _runServerTask;
        Task<Result> _runClientTask;
        List<string> _clientLogMessages;
        List<string> _serverLogMessages;
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
        string _messageFromClient;
        string _messageFromServer;
        string _transferFolderPath;
        string _cidrIp;
        IPAddress _localIp;
        IPAddress _publicIp;
        IPAddress _remoteServerLocalIp;
        IPAddress _remoteServerPublicIp;
        ServerPlatform _remoteServerPlatform;
        ServerPlatform _thisServerPlatform;

        bool _serverReceivedTextMessage;
        bool _serverNoFileTransferPending;
        bool _serverReceivedAllFileBytes;
        bool _serverReceivedConfirmationMessage;
        bool _serverRejectedFileTransfer;
        bool _serverHasNoFilesAvailableToDownload;
        bool _serverTransferFolderDoesNotExist;
        bool _serverErrorOccurred;

        bool _clientReceivedTextMessage;
        bool _clientNoFileTransferPending;
        bool _clientReceivedAllFileBytes;
        bool _clientRejectedFileTransfer;
        bool _clientReceivedConfirmationMessage;
        bool _clientReceivedServerInfo;
        bool _clientReceivedFileInfoList;
        bool _clientErrorOccurred;

        [TestInitialize]
        public async Task Setup()
        {
            _messageFromClient = string.Empty;
            _messageFromServer = string.Empty;
            _transferFolderPath = string.Empty;
            _remoteServerLocalIp = null;
            _remoteServerPublicIp = null;

            _clientLogMessages = new List<string>();
            _serverLogMessages = new List<string>();
            _clientLogFilePath = string.Empty;
            _serverLogFilePath = string.Empty;

            _serverReceivedTextMessage = false;
            _serverNoFileTransferPending = true;
            _serverReceivedAllFileBytes = false;
            _serverReceivedConfirmationMessage = false;
            _serverRejectedFileTransfer = false;
            _serverHasNoFilesAvailableToDownload = false;
            _serverTransferFolderDoesNotExist = false;
            _serverErrorOccurred = false;

            _clientReceivedTextMessage = false;
            _clientNoFileTransferPending = true;
            _clientReceivedAllFileBytes = false;
            _clientReceivedConfirmationMessage = false;
            _clientRejectedFileTransfer = false;
            _clientReceivedServerInfo = false;
            _clientReceivedAllFileBytes = false;
            _clientReceivedFileInfoList = false;
            _clientErrorOccurred = false;

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
            _publicIp = IPAddress.None;
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

            var getPublicIpResult = await NetworkUtilities.GetPublicIPv4AddressAsync().ConfigureAwait(false);
            if (getPublicIpResult.Success)
            {
                _publicIp = getPublicIpResult.Value;
            }

            _cts = new CancellationTokenSource();

            _socketSettings = new SocketSettings
            {
                ListenBacklogSize = 1,
                BufferSize = 1024,
                SocketTimeoutInMilliseconds = 5000
            };

            var clientSettings = new ServerSettings
            {
                LocalServerFolderPath = _localFolder,
                LocalNetworkCidrIp = _cidrIp,
                SocketSettings = _socketSettings
            };

            var serverSettings = new ServerSettings
            {
                LocalServerFolderPath = _remoteFolder,
                LocalNetworkCidrIp = _cidrIp,
                SocketSettings = _socketSettings
            };

            _server = new AsyncFileServer("Server", serverSettings);
            _client = new AsyncFileServer("Client", clientSettings);

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

                await _server.ShutdownAsync().ConfigureAwait(false);
                if (_runServerTask == await Task.WhenAny(_runServerTask, Task.Delay(1000)).ConfigureAwait(false))
                {
                    runServerResult = await _runServerTask;
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

            const int localPort = 8001;
            const int remoteServerPort = 8002;
            const string messageForServer = "Hello, fellow TPL $ocket Server! This is a text message with a few special ch@r@cters. `~/|\\~'";
            const string messageForClient = "I don't know who or what you are referring to. I am a normal human, sir, and most definitely NOT some type of server. Good day.";

            await _server.InitializeAsync(_cidrIp,  remoteServerPort).ConfigureAwait(false);
            _server.EventOccurred += HandleServerEvent;
            _server.SocketEventOccurred += HandleServerEvent;

            await _client.InitializeAsync(_cidrIp,  localPort).ConfigureAwait(false);
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

            Assert.AreEqual(string.Empty, _messageFromClient);
            Assert.AreEqual(string.Empty, _messageFromServer);

            var sendMessageResult1 =
                await _client.SendTextMessageAsync(
                        messageForServer,
                        _localIp,
                        remoteServerPort)
                    .ConfigureAwait(false);

            if (sendMessageResult1.Failure)
            {
                Assert.Fail($"There was an error sending a text message to the server: {sendMessageResult1.Error}");
            }

            while (!_serverReceivedTextMessage) { }

            Assert.AreEqual(messageForServer, _messageFromClient);
            Assert.AreEqual(string.Empty, _messageFromServer);

            var sendMessageResult2 =
                await _server.SendTextMessageAsync(
                        messageForClient,
                        _localIp,
                        localPort)
                    .ConfigureAwait(false);

            if (sendMessageResult2.Failure)
            {
                Assert.Fail($"There was an error sending a text message to the client: {sendMessageResult2.Error}");
            }

            while (!_clientReceivedTextMessage) { }

            Assert.AreEqual(messageForServer, _messageFromClient);
            Assert.AreEqual(messageForClient, _messageFromServer);
        }

        [TestMethod]
        public async Task VerifySendFile()
        {
            _clientLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifySendFile_client.log";
            _serverLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifySendFile_server.log";

            const int remoteServerPort = 8003;
            const int localPort = 8004;

            var sendFilePath = _localFilePath;
            var receiveFilePath = _remoteFilePath;
            var receiveFolderPath = _remoteFolder;

            await _server.InitializeAsync(_cidrIp,  remoteServerPort).ConfigureAwait(false);
            _server.EventOccurred += HandleServerEvent;
            _server.SocketEventOccurred += HandleServerEvent;

            await _client.InitializeAsync(_cidrIp,  localPort).ConfigureAwait(false);
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
                    remoteServerPort,
                    _server.MyInfo.Name,
                    sendFilePath,
                    receiveFolderPath).ConfigureAwait(false);

            if (sendFileResult.Failure)
            {
                Assert.Fail("There was an error sending the file to the remote server: " + sendFileResult.Error);
            }

            while (_serverNoFileTransferPending) { }

            var pendingFileTransferId = _server.PendingFileTransferIds[0];
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
        }

        [TestMethod]
        public async Task VerifyGetFile()
        {
            _clientLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyGetFile_client.log";
            _serverLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyGetFile_server.log";

            const int localPort = 8005;
            const int remoteServerPort = 8006;
            var getFilePath = _remoteFilePath;
            var sentFileSize = new FileInfo(getFilePath).Length;
            var receivedFilePath = _localFilePath;

            await _server.InitializeAsync(_cidrIp,  remoteServerPort).ConfigureAwait(false);
            _server.EventOccurred += HandleServerEvent;
            _server.SocketEventOccurred += HandleServerEvent;

            await _client.InitializeAsync(_cidrIp,  localPort).ConfigureAwait(false);
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
                    remoteServerPort,
                    _server.MyInfo.Name,
                    Path.GetFileName(getFilePath),
                    sentFileSize,
                    Path.GetDirectoryName(getFilePath),
                    _localFolder).ConfigureAwait(false);

            if (getFileResult.Failure)
            {
                var getFileError = "There was an error requesting the file from the remote server: " + getFileResult.Error;
                Assert.Fail(getFileError);
            }

            while (_clientNoFileTransferPending) { }

            var pendingFileTransferId = _client.PendingFileTransferIds[0];
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
        public async Task VerifyRequestServerInfo()
        {
            _clientLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyRequestServerInfo_client.log";
            _serverLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyRequestServerInfo_server.log";

            const int localPort = 8021;
            const int remoteServerPort = 8022;

            await _server.InitializeAsync(_cidrIp,  remoteServerPort).ConfigureAwait(false);
            _server.EventOccurred += HandleServerEvent;
            _server.SocketEventOccurred += HandleServerEvent;

            await _client.InitializeAsync(_cidrIp,  localPort).ConfigureAwait(false);
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
                    remoteServerPort).ConfigureAwait(false);

            if (serverInfoRequest.Failure)
            {
                Assert.Fail("Error sending request for server connection info.");
            }

            while (!_clientReceivedServerInfo) { }

            Assert.AreEqual(_remoteFolder, _transferFolderPath);
            Assert.IsTrue(_remoteServerPublicIp.Equals(_publicIp));
            Assert.IsTrue(_remoteServerLocalIp.Equals(_localIp));
            Assert.AreEqual(_thisServerPlatform, _remoteServerPlatform);
        }

        [TestMethod]
        public async Task VerifyRequestFileList()
        {
            _clientLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyRequestFileList_client.log";
            _serverLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyRequestFileList_server.log";

            const int localPort = 8011;
            const int remoteServerPort = 8012;

            await _server.InitializeAsync(_cidrIp,  remoteServerPort).ConfigureAwait(false);
            _server.EventOccurred += HandleServerEvent;
            _server.SocketEventOccurred += HandleServerEvent;

            await _client.InitializeAsync(_cidrIp,  localPort).ConfigureAwait(false);
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
                        remoteServerPort,
                        _testFilesFolder).ConfigureAwait(false);

            if (fileListRequest.Failure)
            {
                Assert.Fail("Error sending request for transfer folder path.");
            }

            while (!_clientReceivedFileInfoList) { }

            var fileInfoList = _client.RemoteServerFileList;
            Assert.AreEqual(4, fileInfoList.Count);

            var fiDictionaryActual = new Dictionary<string, long>();
            foreach (var (fileName, folderPath, fileSizeBytes) in fileInfoList)
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

            const int remoteServerPort = 8013;
            const int localPort = 8014;

            await _server.InitializeAsync(_cidrIp,  remoteServerPort).ConfigureAwait(false);
            _server.EventOccurred += HandleServerEvent;
            _server.SocketEventOccurred += HandleServerEvent;

            await _client.InitializeAsync(_cidrIp,  localPort).ConfigureAwait(false);
            _client.EventOccurred += HandleClientEvent;
            _client.SocketEventOccurred += HandleClientEvent;

            var token = _cts.Token;

            var sendFilePath = _localFilePath;
            var receiveFilePath = _remoteFilePath;
            var receiveFolderPath = _remoteFolder;

            _runServerTask =
                Task.Run(() =>
                    _server.RunAsync(token), token);

            _runClientTask =
                Task.Run(() =>
                    _client.RunAsync(token), token);

            while (!_server.IsListening) { }
            while (!_client.IsListening) { }

            Assert.IsTrue(File.Exists(receiveFilePath));

            var sendFileResult1 =
                await _client.SendFileAsync(
                    _localIp,
                    remoteServerPort,
                    _server.MyInfo.Name,
                    sendFilePath,
                    receiveFolderPath).ConfigureAwait(false);

            if (sendFileResult1.Failure)
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

            const int localPort = 8015;
            const int remoteServerPort = 8016;
            var getFilePath = _remoteFilePath;
            var sentFileSize = new FileInfo(getFilePath).Length;
            var receivedFilePath = _localFilePath;

            await _server.InitializeAsync(_cidrIp,  remoteServerPort).ConfigureAwait(false);
            _server.EventOccurred += HandleServerEvent;
            _server.SocketEventOccurred += HandleServerEvent;

            await _client.InitializeAsync(_cidrIp,  localPort).ConfigureAwait(false);
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

            var getFileResult1 =
                await _client.GetFileAsync(
                    _localIp,
                    remoteServerPort,
                    _server.MyInfo.Name,
                    Path.GetFileName(getFilePath),
                    sentFileSize,
                    Path.GetDirectoryName(getFilePath),
                    _localFolder).ConfigureAwait(false);

            if (getFileResult1.Failure)
            {
                Assert.Fail("There was an error requesting the file from the remote server: " + getFileResult1.Error);
            }
            
            while (!_clientRejectedFileTransfer) { }
        }

        [TestMethod]
        public async Task VerifyNoFilesAvailableToDownload()
        {
            _clientLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyNoFilesAvailableToDownload_client.log";
            _serverLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyNoFilesAvailableToDownload_server.log";

            const int localPort = 8017;
            const int remoteServerPort = 8018;

            await _server.InitializeAsync(_cidrIp,  remoteServerPort).ConfigureAwait(false);
            _server.EventOccurred += HandleServerEvent;
            _server.SocketEventOccurred += HandleServerEvent;

            await _client.InitializeAsync(_cidrIp,  localPort).ConfigureAwait(false);
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
                    remoteServerPort,
                    _emptyFolder).ConfigureAwait(false);

            if (fileListRequest1.Failure)
            {
                Assert.Fail("Error sending request for transfer folder path.");
            }

            while (!_serverHasNoFilesAvailableToDownload) { }

            var fileInfoList = _client.RemoteServerFileList;
            Assert.AreEqual(0, fileInfoList.Count);

            var fileListRequest2 =
                await _client.RequestFileListAsync(
                    _localIp,
                    remoteServerPort,
                    _testFilesFolder).ConfigureAwait(false);

            if (fileListRequest2.Failure)
            {
                Assert.Fail("Error sending request for transfer folder path.");
            }

            while (!_clientReceivedFileInfoList) { }

            fileInfoList = _client.RemoteServerFileList;
            Assert.AreEqual(4, fileInfoList.Count);

            var fiDictionaryActual = new Dictionary<string, long>();
            foreach (var (fileName, folderPath, fileSizeBytes) in fileInfoList)
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

            const int localPort = 8019;
            const int remoteServerPort = 8020;

            await _server.InitializeAsync(_cidrIp,  remoteServerPort).ConfigureAwait(false);
            _server.EventOccurred += HandleServerEvent;
            _server.SocketEventOccurred += HandleServerEvent;

            await _client.InitializeAsync(_cidrIp,  localPort).ConfigureAwait(false);
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
                    remoteServerPort,
                    _tempFolder).ConfigureAwait(false);

            if (fileListRequest1.Failure)
            {
                Assert.Fail("Error sending request for transfer folder path.");
            }

            while (!_serverTransferFolderDoesNotExist) { }

            var fileInfoList = _client.RemoteServerFileList;
            Assert.AreEqual(0, fileInfoList.Count);

            var fileListRequest2 =
                await _client.RequestFileListAsync(
                    _localIp,
                    remoteServerPort,
                    _testFilesFolder).ConfigureAwait(false);

            if (fileListRequest2.Failure)
            {
                Assert.Fail("Error sending request for transfer folder path.");
            }

            while (!_clientReceivedFileInfoList) { }

            fileInfoList = _client.RemoteServerFileList;
            Assert.AreEqual(4, fileInfoList.Count);

            var fiDictionaryActual = new Dictionary<string, long>();
            foreach (var (fileName, folderPath, fileSizeBytes) in fileInfoList)
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
                case ServerEventType.QueueContainsUnhandledRequests:
                    _clientNoFileTransferPending = false;
                    break;

                case ServerEventType.ReceivedTextMessage:
                    _clientReceivedTextMessage = true;
                    _messageFromServer = serverEvent.TextMessage;
                    break;

                case ServerEventType.ReceivedServerInfo:
                    _transferFolderPath = serverEvent.RemoteFolder;
                    _remoteServerPublicIp = serverEvent.PublicIpAddress;
                    _remoteServerLocalIp = serverEvent.LocalIpAddress;
                    _remoteServerPlatform = serverEvent.RemoteServerPlatform;
                    _clientReceivedServerInfo = true;
                    break;

                case ServerEventType.ReceivedFileList:
                    _clientReceivedFileInfoList = true;
                    break;

                case ServerEventType.ReceiveFileBytesComplete:
                    _clientReceivedAllFileBytes = true;
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
                case ServerEventType.QueueContainsUnhandledRequests:
                    _serverNoFileTransferPending = false;
                    break;

                case ServerEventType.ReceivedTextMessage:
                    _serverReceivedTextMessage = true;
                    _messageFromClient = serverEvent.TextMessage;
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
