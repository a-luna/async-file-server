using System.Linq;

namespace TplSocketServerTest
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Threading;
    using System.Threading.Tasks;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    using AaronLuna.Common.IO;
    using AaronLuna.Common.Logging;
    using AaronLuna.Common.Network;
    using AaronLuna.Common.Result;

    using TplSockets;

    [TestClass]
    public class TplSocketServerTestFixture
    {
        const bool GenerateLogFiles = false;

        const string FileName = "smallFile.jpg";

        CancellationTokenSource _cts;
        SocketSettings _socketSettings;
        TplSocketServer _server;
        TplSocketServer _client;
        Task<Result> _runServerTask;
        Task<Result> _runClientTask;
        IPAddress _localIp;

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
        string _publicIp;
        List<(string, long)> _fileInfoList;
        
        bool _serverReceivedTextMessage;
        bool _serverReceivedAllFileBytes;
        bool _serverReceivedConfirmationMessage;
        bool _serverRejectedFileTransfer;
        bool _serverHasNoFilesAvailableToDownload;
        bool _serverTransferFolderDoesNotExist;
        bool _serverErrorOccurred;
        
        bool _clientReceivedTextMessage;
        bool _clientReceivedAllFileBytes;
        bool _clientRejectedFileTransfer;
        bool _clientReceivedConfirmationMessage;
        bool _clientReceivedTransferFolderPath;
        bool _clientReceivedPublicIp;
        bool _clientReceivedFileInfoList;
        bool _clientErrorOccurred;

        [TestInitialize]
        public void Setup()
        {
            _messageFromClient = string.Empty;
            _messageFromServer = string.Empty;
            _transferFolderPath = string.Empty;
            _publicIp = string.Empty;
            _fileInfoList = new List<(string, long)>();

            _clientLogMessages = new List<string>();
            _serverLogMessages = new List<string>();
            _clientLogFilePath = string.Empty;
            _serverLogFilePath = string.Empty;
            
            _serverReceivedTextMessage = false;
            _serverReceivedAllFileBytes = false;
            _serverReceivedConfirmationMessage = false;
            _serverRejectedFileTransfer = false;
            _serverHasNoFilesAvailableToDownload = false;
            _serverTransferFolderDoesNotExist = false;
            _serverErrorOccurred = false;
            
            _clientReceivedTextMessage = false;
            _clientReceivedAllFileBytes = false;
            _clientReceivedConfirmationMessage = false;
            _clientRejectedFileTransfer = false;
            _clientReceivedTransferFolderPath = false;
            _clientReceivedAllFileBytes = false;
            _clientReceivedPublicIp = false;
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
            
            FileHelper.DeleteFileIfAlreadyExists(_localFilePath);
            if (File.Exists(_restoreFilePath))
            {
                File.Copy(_restoreFilePath, _localFilePath);
            }

            FileHelper.DeleteFileIfAlreadyExists(_remoteFilePath);
            if (File.Exists(_restoreFilePath))
            {
                File.Copy(_restoreFilePath, _remoteFilePath);
            }

            _localIp = IPAddress.Loopback;
            var getLocalIpResult = NetworkUtilities.GetLocalIPv4Address("172.20.10.1/30");
            if (getLocalIpResult.Success)
            {
                _localIp = getLocalIpResult.Value;
            }
            
            _cts = new CancellationTokenSource();

            _socketSettings = new SocketSettings
            {
                MaxNumberOfConnections = 1,
                BufferSize = 1024,
                ConnectTimeoutMs = 5000,
                ReceiveTimeoutMs = 5000,
                SendTimeoutMs = 5000
            };

            _server = new TplSocketServer();
            _client = new TplSocketServer();
        }

        [TestCleanup]
        public async Task ShutdownServerAndClient()
        {
            try
            {
                var shutdownClientTask = Task.Run(() => _client.ShutdownServerAsync());
                var shutdownServerTask = Task.Run(() => _server.ShutdownServerAsync());

                await Task.WhenAll(
                    _runClientTask,
                    _runServerTask,
                    shutdownServerTask,
                    shutdownClientTask);
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

            //await Task.Delay(500);

            if (GenerateLogFiles)
            {
                File.AppendAllLines(_clientLogFilePath, _clientLogMessages);
                File.AppendAllLines(_serverLogFilePath, _serverLogMessages);
            }
        }

        [TestMethod]
        public async Task VerifySendTextMessage()
        {
            _clientLogFilePath = $"client_{Logging.GetTimeStampForFileName()}_VerifySendTextMessage.log";
            _serverLogFilePath = $"server_{Logging.GetTimeStampForFileName()}_VerifySendTextMessage.log";

            const int localPort = 8001;
            const int remoteServerPort = 8002;
            const string messageForServer = "Hello, fellow TPL $ocket Server! This is a text message with a few special ch@r@cters. `~/|\\~'";
            const string messageForClient = "I don't know who or what you are referring to. I am a normal human, sir, and most definitely NOT some type of server. Good day.";
            
            _server.InitializeServer(_localIp, remoteServerPort);
            _server.SocketSettings = _socketSettings;
            _server.MyTransferFolderPath = _remoteFolder;
            _server.EventOccurred += HandleServerEvent;
            _server.SocketEventOccurred += HandleServerEvent;

            _client.InitializeServer(_localIp, localPort);
            _client.SocketSettings = _socketSettings;
            _client.MyTransferFolderPath = _localFolder;
            _client.EventOccurred += HandleClientEvent;
            _client.SocketEventOccurred += HandleClientEvent;

            var token = _cts.Token;

           _runServerTask =
                Task.Run(() =>
                    _server.RunServerAsync(),
                    token);

           _runClientTask =
                Task.Run(() =>
                    _client.RunServerAsync(),
                    token);

            while (!_server.ServerIsRunning) { }
            while (!_client.ServerIsRunning) { }

            Assert.AreEqual(string.Empty, _messageFromClient);
            Assert.AreEqual(string.Empty, _messageFromServer);

            var sendMessageResult1 =
                await _client.SendTextMessageAsync(messageForServer, _localIp.ToString(), remoteServerPort)
                    .ConfigureAwait(false);

            if (sendMessageResult1.Failure)
            {
                Assert.Fail($"There was an error sending a text message to the server: {sendMessageResult1.Error}");
            }

            while (!_serverReceivedTextMessage) { }

            Assert.AreEqual(messageForServer, _messageFromClient);
            Assert.AreEqual(string.Empty, _messageFromServer);

            await Task.Delay(500);

            var sendMessageResult2 =
                await _server.SendTextMessageAsync(messageForClient, _localIp.ToString(), localPort)
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
            _clientLogFilePath = $"client_{Logging.GetTimeStampForFileName()}_VerifySendFile.log";
            _serverLogFilePath = $"server_{Logging.GetTimeStampForFileName()}_VerifySendFile.log";

            const int remoteServerPort = 8003;
            const int localPort = 8004;

            var sendFilePath = _localFilePath;
            var receiveFilePath = _remoteFilePath;
            var receiveFolderPath = _remoteFolder;

            _server.InitializeServer(_localIp, remoteServerPort);
            _server.SocketSettings = _socketSettings;
            _server.MyTransferFolderPath = _remoteFolder;
            _server.EventOccurred += HandleServerEvent;
            _server.SocketEventOccurred += HandleServerEvent;

            _client.InitializeServer(_localIp, localPort);
            _client.SocketSettings = _socketSettings;
            _client.MyTransferFolderPath = _localFolder;
            _client.EventOccurred += HandleClientEvent;
            _client.SocketEventOccurred += HandleClientEvent;

            var token = _cts.Token;

           _runServerTask =
                Task.Run(() =>
                        _server.RunServerAsync(), token);

           _runClientTask =
                Task.Run(() =>
                        _client.RunServerAsync(), token);

            while (!_server.ServerIsRunning) { }
            while (!_client.ServerIsRunning) { }

            var sizeOfFileToSend = new FileInfo(sendFilePath).Length;
            FileHelper.DeleteFileIfAlreadyExists(receiveFilePath);
            Assert.IsFalse(File.Exists(receiveFilePath));

            var sendFileTask =
                Task.Run(
                    () => _client.SendFileAsync(
                                    _localIp,
                                    remoteServerPort,
                                    sendFilePath,
                                    receiveFolderPath),
                    token);

            while (!_serverReceivedAllFileBytes)
            {
                if (_serverErrorOccurred)
                {
                    Assert.Fail("File transfer failed");
                }
            }

            while (!_clientReceivedConfirmationMessage) { }

            var sendFileResult = await sendFileTask.ConfigureAwait(false);
            if (sendFileResult.Failure)
            {
                Assert.Fail("There was an error sending the file to the remote server: " + sendFileResult.Error);
            }

            Assert.IsTrue(File.Exists(receiveFilePath));
            Assert.AreEqual(FileName, Path.GetFileName(receiveFilePath));

            var receivedFileSize = new FileInfo(receiveFilePath).Length;
            Assert.AreEqual(sizeOfFileToSend, receivedFileSize);
        }

        [TestMethod]
        public async Task VerifyGetFile()
        {
            _clientLogFilePath = $"client_{Logging.GetTimeStampForFileName()}_VerifyGetFile.log";
            _serverLogFilePath = $"server_{Logging.GetTimeStampForFileName()}_VerifyGetFile.log";

            const int localPort = 8005;
            const int remoteServerPort = 8006;
            var getFilePath = _remoteFilePath;
            var receivedFilePath = _localFilePath;

            _server.InitializeServer(_localIp, remoteServerPort);
            _server.SocketSettings = _socketSettings;
            _server.MyTransferFolderPath = _remoteFolder;
            _server.EventOccurred += HandleServerEvent;
            _server.SocketEventOccurred += HandleServerEvent;

            _client.InitializeServer(_localIp, localPort);
            _client.SocketSettings = _socketSettings;
            _client.MyTransferFolderPath = _localFolder;
            _client.EventOccurred += HandleClientEvent;
            _client.SocketEventOccurred += HandleClientEvent;

            var token = _cts.Token;

           _runServerTask =
                Task.Run(() =>
                    _server.RunServerAsync(),
                    token);

           _runClientTask =
                Task.Run(() =>
                    _client.RunServerAsync(),
                    token);

            while (!_server.ServerIsRunning) { }
            while (!_client.ServerIsRunning) { }

            FileHelper.DeleteFileIfAlreadyExists(receivedFilePath);
            Assert.IsFalse(File.Exists(receivedFilePath));

            var getFileResult =
                await _client.GetFileAsync(
                        _localIp.ToString(),
                        remoteServerPort,
                        getFilePath,
                        _localFolder).ConfigureAwait(false);

            if (getFileResult.Failure)
            {
                Assert.Fail("There was an error requesting the file from the remote server: " + getFileResult.Error);
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

            var sentFileSize = new FileInfo(getFilePath).Length;
            var receivedFileSize = new FileInfo(receivedFilePath).Length;
            Assert.AreEqual(sentFileSize, receivedFileSize);
        }

        [TestMethod]
        public async Task VerifyRequestTransferFolder()
        {
            _clientLogFilePath = $"client_{Logging.GetTimeStampForFileName()}_VerifyRequestTransferFolder.log";
            _serverLogFilePath = $"server_{Logging.GetTimeStampForFileName()}_VerifyRequestTransferFolder.log";

            const int localPort = 8007;
            const int remoteServerPort = 8008;

            _server.InitializeServer(_localIp, remoteServerPort);
            _server.SocketSettings = _socketSettings;
            _server.MyTransferFolderPath = _remoteFolder;
            _server.EventOccurred += HandleServerEvent;
            _server.SocketEventOccurred += HandleServerEvent;

            _client.InitializeServer(_localIp, localPort);
            _client.SocketSettings = _socketSettings;
            _client.MyTransferFolderPath = _localFolder;
            _client.EventOccurred += HandleClientEvent;
            _client.SocketEventOccurred += HandleClientEvent;

            _server.MyTransferFolderPath = _remoteFolder;
            var token = _cts.Token;

           _runServerTask =
                Task.Run(() =>
                        _server.RunServerAsync(),
                        token);

           _runClientTask =
                Task.Run(() =>
                        _client.RunServerAsync(),
                        token);

            while (!_server.ServerIsRunning) { }
            while (!_client.ServerIsRunning) { }

            Assert.AreEqual(string.Empty, _transferFolderPath);

            var transferFolderRequest =
               await _client.RequestTransferFolderPathAsync(
                    _localIp.ToString(),
                    remoteServerPort).ConfigureAwait(false);

            if (transferFolderRequest.Failure)
            {
                Assert.Fail("Error sending request for transfer folder path.");
            }

            while(!_clientReceivedTransferFolderPath) { }
            Assert.AreEqual(_remoteFolder, _transferFolderPath);
        }

        [TestMethod]
        public async Task VerifyRequestPublicIp()
        {
            _clientLogFilePath = $"client_{Logging.GetTimeStampForFileName()}_VerifyRequestPublicIp.log";
            _serverLogFilePath = $"server_{Logging.GetTimeStampForFileName()}_VerifyRequestPublicIp.log";

            const int localPort = 8009;
            const int remoteServerPort = 8010;

            _server.InitializeServer(_localIp, remoteServerPort);
            _server.SocketSettings = _socketSettings;
            _server.MyTransferFolderPath = _remoteFolder;
            _server.EventOccurred += HandleServerEvent;
            _server.SocketEventOccurred += HandleServerEvent;

            _client.InitializeServer(_localIp, localPort);
            _client.SocketSettings = _socketSettings;
            _client.MyTransferFolderPath = _localFolder;
            _client.EventOccurred += HandleClientEvent;
            _client.SocketEventOccurred += HandleClientEvent;

            var publicIpTask = await NetworkUtilities.GetPublicIPv4AddressAsync();
            if (publicIpTask.Failure)
            {
                Assert.Fail("Unable to determine public IP address, verify internet connection on this machine");
            }

            var publicIp = publicIpTask.Value.ToString();
            var token = _cts.Token;

           _runServerTask =
                Task.Run(() =>
                        _server.RunServerAsync(),
                    token);

           _runClientTask =
                Task.Run(() =>
                        _client.RunServerAsync(),
                    token);

            while (!_server.ServerIsRunning) { }
            while (!_client.ServerIsRunning) { }

            Assert.AreEqual(string.Empty, _publicIp);

            var publicIpRequest =
               await _client.RequestPublicIpAsync(
                    _localIp.ToString(),
                    remoteServerPort).ConfigureAwait(false);

            if (publicIpRequest.Failure)
            {
                Assert.Fail("Error sending request for public IP address.");
            }

            while (!(_clientReceivedPublicIp)) { }
            Assert.AreEqual(publicIp, _publicIp);
        }

        [TestMethod]
        public async Task VerifyRequestFileList()
        {
            _clientLogFilePath = $"client_{Logging.GetTimeStampForFileName()}_VerifyRequestFileList.log";
            _serverLogFilePath = $"server_{Logging.GetTimeStampForFileName()}_VerifyRequestFileList.log";

            const int localPort = 8011;
            const int remoteServerPort = 8012;

            _server.InitializeServer(_localIp, remoteServerPort);
            _server.SocketSettings = _socketSettings;
            _server.MyTransferFolderPath = _testFilesFolder;
            _server.EventOccurred += HandleServerEvent;
            _server.SocketEventOccurred += HandleServerEvent;

            _client.InitializeServer(_localIp, localPort);
            _client.SocketSettings = _socketSettings;
            _client.MyTransferFolderPath = _localFolder;
            _client.EventOccurred += HandleClientEvent;
            _client.SocketEventOccurred += HandleClientEvent;
            
            var token = _cts.Token;

           _runServerTask =
                Task.Run(() =>
                        _server.RunServerAsync(),
                    token);

           _runClientTask =
                Task.Run(() =>
                        _client.RunServerAsync(),
                    token);

            while (!_server.ServerIsRunning) { }
            while (!_client.ServerIsRunning) { }

            var fileListRequest =
                await _client.RequestFileListAsync(
                        _localIp.ToString(),
                        remoteServerPort,
                        _testFilesFolder).ConfigureAwait(false);

            if (fileListRequest.Failure)
            {
                Assert.Fail("Error sending request for transfer folder path.");
            }

            while (!_clientReceivedFileInfoList) { }

            Assert.AreEqual(4, _fileInfoList.Count);
            
            var fiDictionaryActual = new Dictionary<string, long>();
            foreach (var fi in _fileInfoList)
            {
                fiDictionaryActual.Add(fi.Item1, fi.Item2);
            }

            var expectedFileNames = new List<string>();
            expectedFileNames.Add(Path.Combine(_testFilesFolder, "fake.exe"));
            expectedFileNames.Add(Path.Combine(_testFilesFolder, "loremipsum1.txt"));
            expectedFileNames.Add(Path.Combine(_testFilesFolder, "loremipsum2.txt"));
            expectedFileNames.Add(Path.Combine(_testFilesFolder, "smallFile.jpg"));

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
            _clientLogFilePath = $"client_{Logging.GetTimeStampForFileName()}_VerifyOutboundFileTransferRejected.log";
            _serverLogFilePath = $"server_{Logging.GetTimeStampForFileName()}_VerifyOutboundFileTransferRejected.log";

            const int remoteServerPort = 8013;
            const int localPort = 8014;

            _server.InitializeServer(_localIp, remoteServerPort);
            _server.SocketSettings = _socketSettings;
            _server.MyTransferFolderPath = _remoteFolder;
            _server.EventOccurred += HandleServerEvent;
            _server.SocketEventOccurred += HandleServerEvent;

            _client.InitializeServer(_localIp, localPort);
            _client.SocketSettings = _socketSettings;
            _client.MyTransferFolderPath = _localFolder;
            _client.EventOccurred += HandleClientEvent;
            _client.SocketEventOccurred += HandleClientEvent;

            var sendFilePath = _localFilePath;
            var receiveFilePath = _remoteFilePath;
            var receiveFolderPath = _remoteFolder;
            
            _runServerTask =
                Task.Run(() =>
                    _server.RunServerAsync());

            _runClientTask =
                Task.Run(() =>
                    _client.RunServerAsync());

            while (!_server.ServerIsRunning) { }
            while (!_client.ServerIsRunning) { }

            Assert.IsTrue(File.Exists(receiveFilePath));

            var sendFileResult1 =
                await _client.SendFileAsync(
                    _localIp,
                    remoteServerPort,
                    sendFilePath,
                    receiveFolderPath);

            while (!_serverRejectedFileTransfer) { }
            
            if (sendFileResult1.Failure)
            {
                Assert.Fail("Error occurred sending outbound file request to server");
            }

            var sizeOfFileToSend = new FileInfo(sendFilePath).Length;
            FileHelper.DeleteFileIfAlreadyExists(receiveFilePath);
            Assert.IsFalse(File.Exists(receiveFilePath));

            var sendFileResult =
                await _client.SendFileAsync(
                    _localIp,
                    remoteServerPort,
                    sendFilePath,
                    receiveFolderPath);

            while (!_serverReceivedAllFileBytes)
            {
                if (_serverErrorOccurred)
                {
                    Assert.Fail("File transfer failed");
                }
            }

            while (!_clientReceivedConfirmationMessage) { }
            
            if (sendFileResult.Failure)
            {
                Assert.Fail("There was an error sending the file to the remote server: " + sendFileResult.Error);
            }

            Assert.IsTrue(File.Exists(receiveFilePath));
            Assert.AreEqual(FileName, Path.GetFileName(receiveFilePath));

            var receivedFileSize = new FileInfo(receiveFilePath).Length;
            Assert.AreEqual(sizeOfFileToSend, receivedFileSize);
        }

        [TestMethod]
        public async Task VerifyInboundFileTransferRejected()
        {
            _clientLogFilePath = $"client_{Logging.GetTimeStampForFileName()}_VerifyInboundFileTransferRejected.log";
            _serverLogFilePath = $"server_{Logging.GetTimeStampForFileName()}_VerifyInboundFileTransferRejected.log";

            const int localPort = 8015;
            const int remoteServerPort = 8016;
            var getFilePath = _remoteFilePath;
            var receivedFilePath = _localFilePath;

            _server.InitializeServer(_localIp, remoteServerPort);
            _server.SocketSettings = _socketSettings;
            _server.MyTransferFolderPath = _remoteFolder;
            _server.EventOccurred += HandleServerEvent;
            _server.SocketEventOccurred += HandleServerEvent;

            _client.InitializeServer(_localIp, localPort);
            _client.SocketSettings = _socketSettings;
            _client.MyTransferFolderPath = _localFolder;
            _client.EventOccurred += HandleClientEvent;
            _client.SocketEventOccurred += HandleClientEvent;

            var token = _cts.Token;

            _runServerTask =
                Task.Run(() =>
                        _server.RunServerAsync(),
                    token);

            _runClientTask =
                Task.Run(() =>
                        _client.RunServerAsync(),
                    token);

            while (!_server.ServerIsRunning) { }
            while (!_client.ServerIsRunning) { }

            Assert.IsTrue(File.Exists(receivedFilePath));

            var getFileResult1 =
                await _client.GetFileAsync(
                            _localIp.ToString(),
                            remoteServerPort,
                            getFilePath,
                            _localFolder).ConfigureAwait(false);

            if (getFileResult1.Failure)
            {
                Assert.Fail("There was an error requesting the file from the remote server: " + getFileResult1.Error);
            }

            while (!_clientRejectedFileTransfer) { }

            FileHelper.DeleteFileIfAlreadyExists(receivedFilePath);
            Assert.IsFalse(File.Exists(receivedFilePath));

            var getFileResult2 =
                await _client.GetFileAsync(
                            _localIp.ToString(),
                            remoteServerPort,
                            getFilePath,
                            _localFolder).ConfigureAwait(false);

            if (getFileResult2.Failure)
            {
                Assert.Fail("There was an error requesting the file from the remote server: " + getFileResult2.Error);
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

            var sentFileSize = new FileInfo(getFilePath).Length;
            var receivedFileSize = new FileInfo(receivedFilePath).Length;
            Assert.AreEqual(sentFileSize, receivedFileSize);
        }

        [TestMethod]
        public async Task VerifyNoFilesAvailableToDownload()
        {
            _clientLogFilePath = $"client_{Logging.GetTimeStampForFileName()}_VerifyNoFilesAvailableToDownload.log";
            _serverLogFilePath = $"server_{Logging.GetTimeStampForFileName()}_VerifyNoFilesAvailableToDownload.log";

            const int localPort = 8017;
            const int remoteServerPort = 8018;

            _server.InitializeServer(_localIp, remoteServerPort);
            _server.SocketSettings = _socketSettings;
            _server.MyTransferFolderPath = _remoteFolder;
            _server.EventOccurred += HandleServerEvent;
            _server.SocketEventOccurred += HandleServerEvent;

            _client.InitializeServer(_localIp, localPort);
            _client.SocketSettings = _socketSettings;
            _client.MyTransferFolderPath = _localFolder;
            _client.EventOccurred += HandleClientEvent;
            _client.SocketEventOccurred += HandleClientEvent;

            var token = _cts.Token;

            _runServerTask =
                Task.Run(() =>
                        _server.RunServerAsync(),
                    token);

            _runClientTask =
                Task.Run(() =>
                        _client.RunServerAsync(),
                    token);

            while (!_server.ServerIsRunning) { }
            while (!_client.ServerIsRunning) { }

            var fileListRequest1 =
                await _client.RequestFileListAsync(
                    _localIp.ToString(),
                    remoteServerPort,
                    _emptyFolder).ConfigureAwait(false);

            if (fileListRequest1.Failure)
            {
                Assert.Fail("Error sending request for transfer folder path.");
            }

            while (!_serverHasNoFilesAvailableToDownload) { }

            Assert.AreEqual(0, _fileInfoList.Count);

            var fileListRequest2 =
                await _client.RequestFileListAsync(
                    _localIp.ToString(),
                    remoteServerPort,
                    _testFilesFolder).ConfigureAwait(false);

            if (fileListRequest2.Failure)
            {
                Assert.Fail("Error sending request for transfer folder path.");
            }

            while (!_clientReceivedFileInfoList) { }

            Assert.AreEqual(4, _fileInfoList.Count);

            var fiDictionaryActual = new Dictionary<string, long>();
            foreach (var fi in _fileInfoList)
            {
                fiDictionaryActual.Add(fi.Item1, fi.Item2);
            }

            var expectedFileNames = new List<string>();
            expectedFileNames.Add(Path.Combine(_testFilesFolder, "fake.exe"));
            expectedFileNames.Add(Path.Combine(_testFilesFolder, "loremipsum1.txt"));
            expectedFileNames.Add(Path.Combine(_testFilesFolder, "loremipsum2.txt"));
            expectedFileNames.Add(Path.Combine(_testFilesFolder, "smallFile.jpg"));

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
            _clientLogFilePath = $"client_{Logging.GetTimeStampForFileName()}_VerifyRequestedFolderDoesNotExist.log";
            _serverLogFilePath = $"server_{Logging.GetTimeStampForFileName()}_VerifyRequestedFolderDoesNotExist.log";

            const int localPort = 8019;
            const int remoteServerPort = 8020;

            _server.InitializeServer(_localIp, remoteServerPort);
            _server.SocketSettings = _socketSettings;
            _server.MyTransferFolderPath = _remoteFolder;
            _server.EventOccurred += HandleServerEvent;
            _server.SocketEventOccurred += HandleServerEvent;

            _client.InitializeServer(_localIp, localPort);
            _client.SocketSettings = _socketSettings;
            _client.MyTransferFolderPath = _localFolder;
            _client.EventOccurred += HandleClientEvent;
            _client.SocketEventOccurred += HandleClientEvent;

            var token = _cts.Token;
            
            _runServerTask =
                Task.Run(() =>
                        _server.RunServerAsync(),
                    token);

            _runClientTask =
                Task.Run(() =>
                        _client.RunServerAsync(),
                    token);

            while (!_server.ServerIsRunning) { }
            while (!_client.ServerIsRunning) { }
            
            Assert.IsFalse(Directory.Exists(_tempFolder));

            var fileListRequest1 =
                await _client.RequestFileListAsync(
                    _localIp.ToString(),
                    remoteServerPort,
                    _tempFolder).ConfigureAwait(false);

            if (fileListRequest1.Failure)
            {
                Assert.Fail("Error sending request for transfer folder path.");
            }

            while (!_serverTransferFolderDoesNotExist) { }

            Assert.AreEqual(0, _fileInfoList.Count);

            var fileListRequest2 =
                await _client.RequestFileListAsync(
                    _localIp.ToString(),
                    remoteServerPort,
                    _testFilesFolder).ConfigureAwait(false);

            if (fileListRequest2.Failure)
            {
                Assert.Fail("Error sending request for transfer folder path.");
            }

            while (!_clientReceivedFileInfoList) { }

            Assert.AreEqual(4, _fileInfoList.Count);

            var fiDictionaryActual = new Dictionary<string, long>();
            foreach (var fi in _fileInfoList)
            {
                fiDictionaryActual.Add(fi.Item1, fi.Item2);
            }

            var expectedFileNames = new List<string>();
            expectedFileNames.Add(Path.Combine(_testFilesFolder, "fake.exe"));
            expectedFileNames.Add(Path.Combine(_testFilesFolder, "loremipsum1.txt"));
            expectedFileNames.Add(Path.Combine(_testFilesFolder, "loremipsum2.txt"));
            expectedFileNames.Add(Path.Combine(_testFilesFolder, "smallFile.jpg"));

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
            var logMessage = 
                $"(client)\t{DateTime.Now:MM/dd/yyyy HH:mm:ss.fff}\t{serverEvent}";

            Console.WriteLine(logMessage);
            _clientLogMessages.Add(logMessage);

            switch (serverEvent.EventType)
            {
                case EventType.ReceivedTextMessage:
                    _clientReceivedTextMessage = true;
                    _messageFromServer = serverEvent.TextMessage;
                    break;

                case EventType.ReceivedTransferFolderPath:
                    _transferFolderPath = serverEvent.RemoteFolder;
                    _clientReceivedTransferFolderPath = true;
                    break;

                case EventType.ReceivedPublicIpAddress:
                    _publicIp = serverEvent.PublicIpAddress.ToString();
                    _clientReceivedPublicIp = true;
                    break;

                case EventType.ReceivedFileList:
                    _fileInfoList = serverEvent.RemoteServerFileList;
                    _clientReceivedFileInfoList = true;
                    break;

                case EventType.ReceiveFileBytesComplete:
                    _clientReceivedAllFileBytes = true;
                    break;

                case EventType.ClientRejectedFileTransfer:
                    _serverRejectedFileTransfer = true;
                    break;

                case EventType.ReceivedNotificationNoFilesToDownload:
                    _serverHasNoFilesAvailableToDownload = true;
                    break;

                case EventType.ReceivedNotificationFolderDoesNotExist:
                    _serverTransferFolderDoesNotExist = true;
                    break;

                case EventType.ReceiveConfirmationMessageComplete:
                    _clientReceivedConfirmationMessage = true;
                    break;

                case EventType.ErrorOccurred:
                    _clientErrorOccurred = true;
                    break;
            }
        }

        void HandleServerEvent(object sender, ServerEvent serverEvent)
        {
            var logMessage = 
                $"(server)\t{DateTime.Now:MM/dd/yyyy HH:mm:ss.fff}\t{serverEvent}";

            Console.Write(logMessage);
            _serverLogMessages.Add(logMessage);

            switch (serverEvent.EventType)
            {
                case EventType.ReceivedTextMessage:
                    _serverReceivedTextMessage = true;
                    _messageFromClient = serverEvent.TextMessage;
                    break;

                case EventType.ReceiveFileBytesComplete:
                    _serverReceivedAllFileBytes = true;
                    break;

                case EventType.ClientRejectedFileTransfer:
                    _clientRejectedFileTransfer = true;
                    break;

                case EventType.ReceiveConfirmationMessageComplete:
                    _serverReceivedConfirmationMessage = true;
                    break;

                case EventType.ErrorOccurred:
                    _serverErrorOccurred = true;
                    break;
            }
        }
    }
}
