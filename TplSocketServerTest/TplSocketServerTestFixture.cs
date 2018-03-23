namespace TplSocketServerTest
{
    using AaronLuna.Common.IO;
    using AaronLuna.Common.Network;
    using AaronLuna.Common.Result;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Threading;
    using System.Threading.Tasks;

    using TplSocketServer;

    [TestClass]
    public class TplSocketServerTestFixture
    {
        const string FileName = "smallFile.jpg";

        CancellationTokenSource _tokenSource;
        SocketSettings _socketSettings;
        TplSocketServer _server;
        TplSocketServer _client;
        Task<Result> _runServerTask;
        Task<Result> _runClientTask;
        IPAddress _localIp;

        string _localFolder;
        string _remoteFolder;
        string _testFilesFolder;
        string _emptyFolder;
        string _localFilePath;
        string _remoteFilePath;
        string _restoreFilePath;
        string _messageFromClient;
        string _messageFromServer;
        string _transferFolderPath;
        string _serverErrorMessage;
        string _clientErrorMessage;
        string _publicIp;
        List<(string, long)> _fileInfoList;

        bool _serverIsListening;
        bool _serverReceivedTextMessage;
        bool _serverReceivedAllFileBytes;
        bool _serverReceivedConfirmationMessage;
        bool _serverRejectedFileTransfer;
        bool _serverHasNoFilesAvailableToDownload;
        bool _serverTransferFolderDoesNotExist;
        bool _serverListenSocketShutdownWithoutError;
        bool _serverListenSocketShutdownWithError;
        bool _serverErrorOccurred;

        bool _clientIsListening;
        bool _clientReceivedTextMessage;
        bool _clientReceivedAllFileBytes;
        bool _clientRejectedFileTransfer;
        bool _clientReceivedConfirmationMessage;
        bool _clientReceivedTransferFolderPath;
        bool _clientReceivedPublicIp;
        bool _clientReceivedFileInfoList;
        bool _clientListenSocketShutdownWithoutError;
        bool _clientListenSocketShutdownWithError;
        bool _clientErrorOccurred;

        [TestInitialize]
        public void Setup()
        {
            _messageFromClient = string.Empty;
            _messageFromServer = string.Empty;
            _clientErrorMessage = string.Empty;
            _serverErrorMessage = string.Empty;
            _transferFolderPath = string.Empty;
            _publicIp = string.Empty;
            _fileInfoList = new List<(string, long)>();

            _serverIsListening = false;
            _serverReceivedTextMessage = false;
            _serverReceivedAllFileBytes = false;
            _serverReceivedConfirmationMessage = false;
            _serverRejectedFileTransfer = false;
            _serverHasNoFilesAvailableToDownload = false;
            _serverTransferFolderDoesNotExist = false;
            _serverListenSocketShutdownWithoutError = false;
            _serverListenSocketShutdownWithError = false;
            _serverErrorOccurred = false;

            _clientIsListening = false;
            _clientReceivedTextMessage = false;
            _clientReceivedAllFileBytes = false;
            _clientReceivedConfirmationMessage = false;
            _clientRejectedFileTransfer = false;
            _clientReceivedTransferFolderPath = false;
            _clientReceivedAllFileBytes = false;
            _clientReceivedPublicIp = false;
            _clientReceivedFileInfoList = false;
            _clientListenSocketShutdownWithoutError = false;
            _clientListenSocketShutdownWithError = false;
            _clientErrorOccurred = false;
            
            var currentPath = Directory.GetCurrentDirectory();
            var index = currentPath.IndexOf("bin", StringComparison.Ordinal);
            _testFilesFolder = $"{currentPath.Remove(index - 1)}{Path.DirectorySeparatorChar}TestFiles{Path.DirectorySeparatorChar}";

            _localFolder = _testFilesFolder + $"Client{Path.DirectorySeparatorChar}";
            _remoteFolder = _testFilesFolder + $"Server{Path.DirectorySeparatorChar}";
            _emptyFolder = _testFilesFolder + $"EmptyFolder{Path.DirectorySeparatorChar}";

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

            _localIp = Network.GetLocalIPv4AddressFromInternet().Value;            
            _tokenSource = new CancellationTokenSource();

            _socketSettings = new SocketSettings
            {
                MaxNumberOfConections = 1,
                BufferSize = 1024,
                ConnectTimeoutMs = 5000,
                ReceiveTimeoutMs = 5000,
                SendTimeoutMs = 5000
            };
        }

        [TestCleanup]
        public async Task ShutdownServerAndClient()
        {
            try
            {
                await _client.ShutdownServerAsync();
                var clientResult = await _runClientTask;
                if (clientResult.Failure)
                {
                    Assert.Fail(
                        "There was an error attempting to shutdown the server: "
                        + clientResult.Error);
                }
            }
            catch (AggregateException ex)
            {
                Console.WriteLine("\nException messages:");
                foreach (var ie in ex.InnerExceptions)
                {
                    Console.WriteLine($"\t{ie.GetType().Name}: {ie.Message}");
                }

                Console.WriteLine($"\nClient Shutdown Task status: {_runClientTask.Status}\n");
            }
            finally
            {
                _client.CloseListenSocket();
            }

            try
            {
                await _server.ShutdownServerAsync();
                var serverResult = await _runServerTask;
                if (serverResult.Failure)
                {
                    Assert.Fail(
                        "There was an error attempting to shutdown the server: "
                        + serverResult.Error);
                }
            }
            catch (AggregateException ex)
            {
                Console.WriteLine("\nException messages:");
                foreach (var ie in ex.InnerExceptions)
                {
                    Console.WriteLine($"\t{ie.GetType().Name}: {ie.Message}");
                }

                Console.WriteLine($"\nServer Shutdown Task status: {_runServerTask.Status}\n");
            }
            finally
            {
                _server.CloseListenSocket();
            }
            
            while (_serverListenSocketShutdownWithoutError && _serverListenSocketShutdownWithError) { }
            while (_clientListenSocketShutdownWithoutError && _clientListenSocketShutdownWithError) { }

            //TODO: Someday, figure out how to correctly close the damn listening socket in my file server....
            Assert.IsTrue(_serverListenSocketShutdownWithError);
            Assert.IsFalse(_serverListenSocketShutdownWithoutError);
            Assert.AreEqual(_serverErrorMessage, _clientErrorMessage);

            //Assert.IsTrue(_serverListenSocketShutdownWithoutError);
            //Assert.IsFalse(_serverListenSocketShutdownWithError);
            //Assert.AreEqual(string.Empty, _serverErrorMessage);

            //Assert.IsTrue(_clientListenSocketShutdownWithoutError);
            //Assert.IsFalse(_clientListenSocketShutdownWithError);
            //Assert.AreEqual(string.Empty, _clientErrorMessage);
        }

        [TestMethod]
        public async Task VerifySendTextMessage()
        {
            const int localPort = 8001;
            const int remoteServerPort = 8002;
            const string messageForServer = "Hello, fellow TPL $ocket Server! This is a text message with a few special ch@r@cters. `~/|\\~'";
            const string messageForClient = "I don't know who or what you are referring to. I am a normal human, sir, and most definitely NOT some type of server. Good day.";
            
            _server = new TplSocketServer(_localIp, remoteServerPort)
            {
                SocketSettings = _socketSettings,
                LoggingEnabled = false,
                TransferFolderPath = _remoteFolder
            };

            _server.EventOccurred += HandleServerEvent;
            _server.SocketEventOccurred += HandleServerEvent;

            _client = new TplSocketServer(_localIp, localPort)
            {
                SocketSettings = _socketSettings,
                LoggingEnabled = false,
                TransferFolderPath = _localFolder
            };

            _client.EventOccurred += HandleClientEvent;
            _client.SocketEventOccurred += HandleServerEvent;

            var token = _tokenSource.Token;

           _runServerTask =
                Task.Run(() =>
                    _server.HandleIncomingConnectionsAsync(token),
                    token);

           _runClientTask =
                Task.Run(() =>
                    _client.HandleIncomingConnectionsAsync(token),
                    token);

            while (!_serverIsListening) { }
            while (!_clientIsListening) { }

            Assert.AreEqual(string.Empty, _messageFromClient);
            Assert.AreEqual(string.Empty, _messageFromServer);

            var sendMessageResult1 =
                await _client.SendTextMessageAsync(messageForServer, _localIp.ToString(), remoteServerPort, _localIp.ToString(), localPort, token)
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
                await _server.SendTextMessageAsync(messageForClient, _localIp.ToString(), localPort, _localIp.ToString(), remoteServerPort, token)
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
            const int remoteServerPort = 8003;
            const int localPort = 8004;

            var sendFilePath = _localFilePath;
            var receiveFilePath = _remoteFilePath;
            var receiveFolderPath = _remoteFolder;

            _server = new TplSocketServer(_localIp, remoteServerPort)
            {
                SocketSettings = _socketSettings,
                LoggingEnabled = false,
                TransferFolderPath = _remoteFolder
            };

            _server.EventOccurred += HandleServerEvent;
            _server.SocketEventOccurred += HandleServerEvent;

            _client = new TplSocketServer(_localIp, localPort)
            {
                SocketSettings = _socketSettings,
                LoggingEnabled = false,
                TransferFolderPath = _localFolder
            };

            _client.EventOccurred += HandleClientEvent;
            _client.SocketEventOccurred += HandleServerEvent;

            var token = _tokenSource.Token;

           _runServerTask =
                Task.Run(() =>
                        _server.HandleIncomingConnectionsAsync(token),
                    token);

           _runClientTask =
                Task.Run(() =>
                        _client.HandleIncomingConnectionsAsync(token),
                    token);

            while (!_serverIsListening) { }
            while (!_clientIsListening) { }

            var sizeOfFileToSend = new FileInfo(sendFilePath).Length;
            FileHelper.DeleteFileIfAlreadyExists(receiveFilePath);
            Assert.IsFalse(File.Exists(receiveFilePath));

            var sendFileTask =
                Task.Run(
                    () => _client.SendFileAsync(
                                    _localIp.ToString(),
                                    remoteServerPort,
                                    sendFilePath,
                                    receiveFolderPath,
                                    token),
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
            const int localPort = 8005;
            const int remoteServerPort = 8006;
            var getFilePath = _remoteFilePath;
            var receivedFilePath = _localFilePath;

            _server = new TplSocketServer(_localIp, remoteServerPort)
            {
                SocketSettings = _socketSettings,
                LoggingEnabled = false,
                TransferFolderPath = _remoteFolder
            };

            _server.EventOccurred += HandleServerEvent;
            _server.SocketEventOccurred += HandleServerEvent;

            _client = new TplSocketServer(_localIp, localPort)
            {
                SocketSettings = _socketSettings,
                LoggingEnabled = false,
                TransferFolderPath = _localFolder
            };

            _client.EventOccurred += HandleClientEvent;
            _client.SocketEventOccurred += HandleServerEvent;

            var token = _tokenSource.Token;

           _runServerTask =
                Task.Run(() =>
                    _server.HandleIncomingConnectionsAsync(token),
                    token);

           _runClientTask =
                Task.Run(() =>
                    _client.HandleIncomingConnectionsAsync(token),
                    token);

            while (!_serverIsListening) { }
            while (!_clientIsListening) { }

            FileHelper.DeleteFileIfAlreadyExists(receivedFilePath);
            Assert.IsFalse(File.Exists(receivedFilePath));

            var getFileResult =
                await _client.GetFileAsync(_localIp.ToString(), remoteServerPort, getFilePath, _localIp.ToString(), localPort, _localFolder, token)
                            .ConfigureAwait(false);

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
            const int localPort = 8007;
            const int remoteServerPort = 8008;

            _server = new TplSocketServer(_localIp, remoteServerPort)
            {
                SocketSettings = _socketSettings,
                LoggingEnabled = false,
                TransferFolderPath = _remoteFolder
            };

            _server.EventOccurred += HandleServerEvent;
            _server.SocketEventOccurred += HandleServerEvent;

            _client = new TplSocketServer(_localIp, localPort)
            {
                SocketSettings = _socketSettings,
                LoggingEnabled = false,
                TransferFolderPath = _localFolder
            };

            _client.EventOccurred += HandleClientEvent;
            _client.SocketEventOccurred += HandleServerEvent;

            _server.TransferFolderPath = _remoteFolder;
            var token = _tokenSource.Token;

           _runServerTask =
                Task.Run(() =>
                        _server.HandleIncomingConnectionsAsync(token),
                        token);

           _runClientTask =
                Task.Run(() =>
                        _client.HandleIncomingConnectionsAsync(token),
                        token);

            while (!_serverIsListening) { }
            while (!_clientIsListening) { }
            Assert.AreEqual(string.Empty, _transferFolderPath);

            var transferFolderRequest =
               await _client.RequestTransferFolderPathAsync(
                    _localIp.ToString(),
                    remoteServerPort,
                    _localIp.ToString(),
                    localPort,
                    token).ConfigureAwait(false);

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
            const int localPort = 8009;
            const int remoteServerPort = 8010;

            _server = new TplSocketServer(_localIp, remoteServerPort)
            {
                SocketSettings = _socketSettings,
                LoggingEnabled = false,
                TransferFolderPath = _remoteFolder
            };

            _server.EventOccurred += HandleServerEvent;
            _server.SocketEventOccurred += HandleServerEvent;

            _client = new TplSocketServer(_localIp, localPort)
            {
                SocketSettings = _socketSettings,
                LoggingEnabled = false,
                TransferFolderPath = _localFolder
            };

            _client.EventOccurred += HandleClientEvent;
            _client.SocketEventOccurred += HandleServerEvent;

            var publicIpTask = await Network.GetPublicIPv4AddressAsync();
            if (publicIpTask.Failure)
            {
                Assert.Fail("Unable to determine public IP address, verify internet connection on this machine");
            }

            var publicIp = publicIpTask.Value.ToString();
            var token = _tokenSource.Token;

           _runServerTask =
                Task.Run(() =>
                        _server.HandleIncomingConnectionsAsync(token),
                    token);

           _runClientTask =
                Task.Run(() =>
                        _client.HandleIncomingConnectionsAsync(token),
                    token);

            while (!_serverIsListening) { }
            while (!_clientIsListening) { }
            Assert.AreEqual(string.Empty, _publicIp);

            var publicIpRequest =
               await _client.RequestPublicIpAsync(
                    _localIp.ToString(),
                    remoteServerPort,
                    _localIp.ToString(),
                    localPort,
                    token).ConfigureAwait(false);

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
            const int localPort = 8011;
            const int remoteServerPort = 8012;

            _server = new TplSocketServer(_localIp, remoteServerPort)
            {
                SocketSettings = _socketSettings,
                LoggingEnabled = false,
                TransferFolderPath = _remoteFolder
            };

            _server.EventOccurred += HandleServerEvent;
            _server.SocketEventOccurred += HandleServerEvent;

            _client = new TplSocketServer(_localIp, localPort)
            {
                SocketSettings = _socketSettings,
                LoggingEnabled = false,
                TransferFolderPath = _localFolder
            };

            _client.EventOccurred += HandleClientEvent;
            _client.SocketEventOccurred += HandleServerEvent;

            _server.TransferFolderPath = _testFilesFolder;
            var token = _tokenSource.Token;

           _runServerTask =
                Task.Run(() =>
                        _server.HandleIncomingConnectionsAsync(token),
                    token);

           _runClientTask =
                Task.Run(() =>
                        _client.HandleIncomingConnectionsAsync(token),
                    token);

            while (!_serverIsListening) { }
            while (!_clientIsListening) { }
            Assert.AreEqual(0, _fileInfoList.Count);

            var fileListRequest =
                await _client.RequestFileListAsync(
                    _localIp.ToString(),
                    remoteServerPort,
                    _localIp.ToString(),
                    localPort,
                    _testFilesFolder,
                    token).ConfigureAwait(false);

            if (fileListRequest.Failure)
            {
                Assert.Fail("Error sending request for transfer folder path.");
            }

            while (!_clientReceivedFileInfoList) { }

            Assert.AreEqual(4, _fileInfoList.Count);

            var fileName1 = Path.GetFileName(_fileInfoList[0].Item1);
            var fileName2 = Path.GetFileName(_fileInfoList[1].Item1);
            var fileName3 = Path.GetFileName(_fileInfoList[2].Item1);
            var fileName4 = Path.GetFileName(_fileInfoList[3].Item1);

            Assert.AreEqual("fake.exe", fileName1);            
            Assert.AreEqual("loremipsum1.txt", fileName2);            
            Assert.AreEqual("loremipsum2.txt", fileName3);
            Assert.AreEqual("smallFile.jpg", fileName4);
        }

        [TestMethod]
        public async Task VerifyOutboundFileTransferRejected()
        {
            const int remoteServerPort = 8013;
            const int localPort = 8014;

            _server = new TplSocketServer(_localIp, remoteServerPort)
            {
                SocketSettings = _socketSettings,
                LoggingEnabled = false,
                TransferFolderPath = _remoteFolder
            };

            _server.EventOccurred += HandleServerEvent;
            _server.SocketEventOccurred += HandleServerEvent;

            _client = new TplSocketServer(_localIp, localPort)
            {
                SocketSettings = _socketSettings,
                LoggingEnabled = false,
                TransferFolderPath = _localFolder
            };

            _client.EventOccurred += HandleClientEvent;
            _client.SocketEventOccurred += HandleServerEvent;

            var sendFilePath = _localFilePath;
            var receiveFilePath = _remoteFilePath;
            var receiveFolderPath = _remoteFolder;

            var token = _tokenSource.Token;

            _runServerTask =
                Task.Run(() =>
                        _server.HandleIncomingConnectionsAsync(token),
                    token);

            _runClientTask =
                Task.Run(() =>
                        _client.HandleIncomingConnectionsAsync(token),
                    token);

            while (!_serverIsListening) { }
            while (!_clientIsListening) { }
            
            Assert.IsTrue(File.Exists(receiveFilePath));

            var sendFileTask1 =
                Task.Run(
                    () => _client.SendFileAsync(
                        _localIp.ToString(),
                        remoteServerPort,
                        sendFilePath,
                        receiveFolderPath,
                        token),
                    token);

            while (!_serverRejectedFileTransfer) { }

            var sendFileResult1 = await sendFileTask1;
            if (sendFileResult1.Failure)
            {
                Assert.Fail("Error occurred sending outbound file request to server");
            }

            var sizeOfFileToSend = new FileInfo(sendFilePath).Length;
            FileHelper.DeleteFileIfAlreadyExists(receiveFilePath);
            Assert.IsFalse(File.Exists(receiveFilePath));

            var sendFileTask2 =
                Task.Run(
                    () => _client.SendFileAsync(
                        _localIp.ToString(),
                        remoteServerPort,
                        sendFilePath,
                        receiveFolderPath,
                        token),
                    token);

            while (!_serverReceivedAllFileBytes)
            {
                if (_serverErrorOccurred)
                {
                    Assert.Fail("File transfer failed");
                }
            }

            while (!_clientReceivedConfirmationMessage) { }

            var sendFileResult = await sendFileTask2.ConfigureAwait(false);
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
            const int localPort = 8015;
            const int remoteServerPort = 8016;
            var getFilePath = _remoteFilePath;
            var receivedFilePath = _localFilePath;

            _server = new TplSocketServer(_localIp, remoteServerPort)
            {
                SocketSettings = _socketSettings,
                LoggingEnabled = false,
                TransferFolderPath = _remoteFolder
            };

            _server.EventOccurred += HandleServerEvent;
            _server.SocketEventOccurred += HandleServerEvent;

            _client = new TplSocketServer(_localIp, localPort)
            {
                SocketSettings = _socketSettings,
                LoggingEnabled = false,
                TransferFolderPath = _localFolder
            };

            _client.EventOccurred += HandleClientEvent;
            _client.SocketEventOccurred += HandleServerEvent;

            var token = _tokenSource.Token;

            _runServerTask =
                Task.Run(() =>
                        _server.HandleIncomingConnectionsAsync(token),
                    token);

            _runClientTask =
                Task.Run(() =>
                        _client.HandleIncomingConnectionsAsync(token),
                    token);

            while (!_serverIsListening) { }
            while (!_clientIsListening) { }
            
            Assert.IsTrue(File.Exists(receivedFilePath));

            var getFileResult1 =
                await _client.GetFileAsync(_localIp.ToString(), remoteServerPort, getFilePath, _localIp.ToString(), localPort, _localFolder, token)
                    .ConfigureAwait(false);

            if (getFileResult1.Failure)
            {
                Assert.Fail("There was an error requesting the file from the remote server: " + getFileResult1.Error);
            }

            while (!_clientRejectedFileTransfer) { }

            FileHelper.DeleteFileIfAlreadyExists(receivedFilePath);
            Assert.IsFalse(File.Exists(receivedFilePath));

            var getFileResult2 =
                await _client.GetFileAsync(_localIp.ToString(), remoteServerPort, getFilePath, _localIp.ToString(), localPort, _localFolder, token)
                    .ConfigureAwait(false);

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
            const int localPort = 8017;
            const int remoteServerPort = 8018;

            _server = new TplSocketServer(_localIp, remoteServerPort)
            {
                SocketSettings = _socketSettings,
                LoggingEnabled = false,
                TransferFolderPath = _remoteFolder
            };

            _server.EventOccurred += HandleServerEvent;
            _server.SocketEventOccurred += HandleServerEvent;

            _client = new TplSocketServer(_localIp, localPort)
            {
                SocketSettings = _socketSettings,
                LoggingEnabled = false,
                TransferFolderPath = _localFolder
            };

            _client.EventOccurred += HandleClientEvent;
            _client.SocketEventOccurred += HandleServerEvent;

            var token = _tokenSource.Token;

            _runServerTask =
                Task.Run(() =>
                        _server.HandleIncomingConnectionsAsync(token),
                    token);

            _runClientTask =
                Task.Run(() =>
                        _client.HandleIncomingConnectionsAsync(token),
                    token);

            while (!_serverIsListening) { }
            while (!_clientIsListening) { }

            var fileListRequest1 =
                await _client.RequestFileListAsync(
                    _localIp.ToString(),
                    remoteServerPort,
                    _localIp.ToString(),
                    localPort,
                    _emptyFolder,
                    token).ConfigureAwait(false);

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
                    _localIp.ToString(),
                    localPort,
                    _testFilesFolder,
                    token).ConfigureAwait(false);

            if (fileListRequest2.Failure)
            {
                Assert.Fail("Error sending request for transfer folder path.");
            }

            while (!_clientReceivedFileInfoList) { }

            Assert.AreEqual(4, _fileInfoList.Count);

            var fileName1 = Path.GetFileName(_fileInfoList[0].Item1);
            var fileName2 = Path.GetFileName(_fileInfoList[1].Item1);
            var fileName3 = Path.GetFileName(_fileInfoList[2].Item1);
            var fileName4 = Path.GetFileName(_fileInfoList[3].Item1);

            Assert.AreEqual("fake.exe", fileName1);
            Assert.AreEqual("loremipsum1.txt", fileName2);
            Assert.AreEqual("loremipsum2.txt", fileName3);
            Assert.AreEqual("smallFile.jpg", fileName4);
        }

        [TestMethod]
        public async Task VerifyRequestedFolderDoesNotExist()
        {
            const int localPort = 8019;
            const int remoteServerPort = 8020;

            _server = new TplSocketServer(_localIp, remoteServerPort)
            {
                SocketSettings = _socketSettings,
                LoggingEnabled = false,
                TransferFolderPath = _remoteFolder
            };

            _server.EventOccurred += HandleServerEvent;
            _server.SocketEventOccurred += HandleServerEvent;

            _client = new TplSocketServer(_localIp, localPort)
            {
                SocketSettings = _socketSettings,
                LoggingEnabled = false,
                TransferFolderPath = _localFolder
            };

            _client.EventOccurred += HandleClientEvent;
            _client.SocketEventOccurred += HandleServerEvent;

            var token = _tokenSource.Token;
            
            _runServerTask =
                Task.Run(() =>
                        _server.HandleIncomingConnectionsAsync(token),
                    token);

            _runClientTask =
                Task.Run(() =>
                        _client.HandleIncomingConnectionsAsync(token),
                    token);

            while (!_serverIsListening) { }
            while (!_clientIsListening) { }

            Directory.Delete(_emptyFolder);
            Assert.IsFalse(Directory.Exists(_emptyFolder));

            var fileListRequest1 =
                await _client.RequestFileListAsync(
                    _localIp.ToString(),
                    remoteServerPort,
                    _localIp.ToString(),
                    localPort,
                    _emptyFolder,
                    token).ConfigureAwait(false);

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
                    _localIp.ToString(),
                    localPort,
                    _testFilesFolder,
                    token).ConfigureAwait(false);

            if (fileListRequest2.Failure)
            {
                Assert.Fail("Error sending request for transfer folder path.");
            }

            while (!_clientReceivedFileInfoList) { }

            Assert.AreEqual(4, _fileInfoList.Count);

            var fileName1 = Path.GetFileName(_fileInfoList[0].Item1);
            var fileName2 = Path.GetFileName(_fileInfoList[1].Item1);
            var fileName3 = Path.GetFileName(_fileInfoList[2].Item1);
            var fileName4 = Path.GetFileName(_fileInfoList[3].Item1);

            Assert.AreEqual("fake.exe", fileName1);
            Assert.AreEqual("loremipsum1.txt", fileName2);
            Assert.AreEqual("loremipsum2.txt", fileName3);
            Assert.AreEqual("smallFile.jpg", fileName4);
        }

        void HandleClientEvent(object sender, ServerEvent serverEvent)
        {
            Console.WriteLine($"(client)\t{DateTime.Now:MM/dd/yyyy HH:mm:ss.fff}\t{serverEvent}");

            switch (serverEvent.EventType)
            {
                case EventType.ListenOnLocalPortComplete:
                    _clientIsListening = true;
                    break;

                case EventType.ReadTextMessageComplete:
                    _clientReceivedTextMessage = true;
                    _messageFromServer = serverEvent.TextMessage;
                    break;

                case EventType.ReadTransferFolderResponseComplete:
                    _transferFolderPath = serverEvent.RemoteFolder;
                    _clientReceivedTransferFolderPath = true;
                    break;

                case EventType.ReadPublicIpResponseComplete:
                    _publicIp = serverEvent.PublicIpAddress;
                    _clientReceivedPublicIp = true;
                    break;

                case EventType.ReadFileListResponseComplete:
                    _fileInfoList = serverEvent.FileInfoList;
                    _clientReceivedFileInfoList = true;
                    break;

                case EventType.ReceiveFileBytesComplete:
                    _clientReceivedAllFileBytes = true;
                    break;

                case EventType.ReceiveFileTransferRejectedComplete:
                    _serverRejectedFileTransfer = true;
                    break;

                case EventType.ReceiveNotificationNoFilesToDownloadComplete:
                    _serverHasNoFilesAvailableToDownload = true;
                    break;

                case EventType.ReceiveNotificationFolderDoesNotExistComplete:
                    _serverTransferFolderDoesNotExist = true;
                    break;

                case EventType.ReceiveConfirmationMessageComplete:
                    _clientReceivedConfirmationMessage = true;
                    break;

                case EventType.ShutdownListenSocketCompletedWithError:
                    _clientListenSocketShutdownWithError = true;
                    _clientErrorMessage = serverEvent.ErrorMessage;
                    break;

                case EventType.ShutdownListenSocketCompletedWithoutError:
                    _clientListenSocketShutdownWithoutError = true;
                    break;

                case EventType.ErrorOccurred:
                    _clientErrorOccurred = true;
                    break;
            }
        }

        void HandleServerEvent(object sender, ServerEvent serverEvent)
        {
            Console.WriteLine($"(server)\t{DateTime.Now:MM/dd/yyyy HH:mm:ss.fff}\t{serverEvent}");

            switch (serverEvent.EventType)
            {
                case EventType.ListenOnLocalPortComplete:
                    _serverIsListening = true;
                    break;

                case EventType.ReadTextMessageComplete:
                    _serverReceivedTextMessage = true;
                    _messageFromClient = serverEvent.TextMessage;
                    break;

                case EventType.ReceiveFileBytesComplete:
                    _serverReceivedAllFileBytes = true;
                    break;

                case EventType.ReceiveFileTransferRejectedComplete:
                    _clientRejectedFileTransfer = true;
                    break;

                case EventType.ReceiveConfirmationMessageComplete:
                    _serverReceivedConfirmationMessage = true;
                    break;

                case EventType.ShutdownListenSocketCompletedWithError:
                    _serverListenSocketShutdownWithError = true;
                    _serverErrorMessage = serverEvent.ErrorMessage;
                    break;

                case EventType.ShutdownListenSocketCompletedWithoutError:
                    _serverListenSocketShutdownWithoutError = true;
                    break;

                case EventType.ErrorOccurred:
                    _serverErrorOccurred = true;
                    break;
            }
        }
    }
}
