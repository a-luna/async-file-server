using System.Net;

namespace TplSocketServerTest
{
    using AaronLuna.Common.IO;
    using AaronLuna.Common.Network;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    using System;
    using System.IO;
    using System.Threading;
    using System.Threading.Tasks;

    using TplSocketServer;

    [TestClass]
    public class TplSocketServerTestFixture
    {
        private const string FileName = "smallFile.jpg";

        private CancellationTokenSource _tokenSource;

        private TplSocketServer _server;
        private TplSocketServer _client;

        private IPAddress _ipAddress;

        private string _localFolder;
        private string _remoteFolder;
        private string _restoreFolder;
        private string _localFilePath;
        private string _remoteFilePath;
        private string _restoreFilePath;
        private string _messageFromClient;
        private string _messageFromServer;
        private string _transferFolderPath;

        private bool _serverIsListening;
        private bool _serverReceivedTextMessage;
        private bool _serverReceivedAllFileBytes;
        private bool _serverReceivedConfirmationMessage;
        private bool _serverListenSocketIsShutdown;
        private bool _serverErrorOccurred;

        private bool _clientIsListening;
        private bool _clientReceivedTextMessage;
        private bool _clientReceivedAllFileBytes;
        private bool _clientReceivedConfirmationMessage;
        private bool _clientReceivedTransferFolderPath;
        private bool _clientListenSocketIsShutdown;
        private bool _clientErrorOccurred;

        [TestInitialize]
        public void Setup()
        {
            _tokenSource = new CancellationTokenSource();

            _server = new TplSocketServer();
            _server.EventOccurred += HandleServerEvent;

            _client = new TplSocketServer();
            _client.EventOccurred += HandleClientEvent;

            _ipAddress = Network.GetLocalIPv4AddressFromInternet().Value;

            var currentPath = Directory.GetCurrentDirectory();
            var index = currentPath.IndexOf(@"bin", StringComparison.Ordinal);
            _restoreFolder = $"{currentPath.Remove(index - 1)}{Path.DirectorySeparatorChar}TestFiles{Path.DirectorySeparatorChar}";

            _localFolder = _restoreFolder + $"Client{Path.DirectorySeparatorChar}";
            _remoteFolder = _restoreFolder + $"Server{Path.DirectorySeparatorChar}";

            _localFilePath = _localFolder + FileName;
            _remoteFilePath = _remoteFolder + FileName;
            _restoreFilePath = _restoreFolder + FileName;

            _messageFromClient = string.Empty;
            _messageFromServer = string.Empty;
            _transferFolderPath = string.Empty;

            _serverIsListening = false;
            _serverReceivedTextMessage = false;
            _serverReceivedAllFileBytes = false;
            _serverReceivedConfirmationMessage = false;
            _serverListenSocketIsShutdown = false;
            _serverErrorOccurred = false;

            _clientIsListening = false;
            _clientReceivedTextMessage = false;
            _clientReceivedAllFileBytes = false;
            _clientReceivedConfirmationMessage = false;
            _clientReceivedTransferFolderPath = false;
            _clientReceivedAllFileBytes = false;
            _clientErrorOccurred = false;

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
        }

        [TestMethod]
        public async Task VerifySendTextMessage()
        {
            var localPort = 8001;
            var remoteServerPort = 8002;
            var messageForServer = "Hello, fellow TPL $ocket Server! This is a text message with a few special ch@r@cters. `~/|\\~'";
            var messageForClient = "I don't know who or what you are referring to. I am a normal human, sir, and most definitely NOT some type of server. Good day.";

            var token = _tokenSource.Token;

            var runServerTask1 =
                Task.Run(() =>
                    _server.HandleIncomingConnectionsAsync(
                        remoteServerPort,
                        token),
                    token);

            var runServerTask2 =
                Task.Run(() =>
                    _client.HandleIncomingConnectionsAsync(
                        localPort,
                        token),
                    token);

            while (!_serverIsListening) { }
            while (!_clientIsListening) { }

            Assert.AreEqual(string.Empty, _messageFromClient);
            Assert.AreEqual(string.Empty, _messageFromServer);

            var sendMessageResult1 =
                await _client.SendTextMessageAsync(messageForServer, _ipAddress.ToString(), remoteServerPort, _ipAddress.ToString(), localPort, token)
                    .ConfigureAwait(false);

            if (sendMessageResult1.Failure)
            {
                Assert.Fail($"There was an error sending a text message to the server: {sendMessageResult1.Error}");
            }

            while (!_serverReceivedTextMessage) { }

            Assert.AreEqual(messageForServer, _messageFromClient);
            Assert.AreEqual(string.Empty, _messageFromServer);

            var sendMessageResult2 =
                await _server.SendTextMessageAsync(messageForClient, _ipAddress.ToString(), localPort, _ipAddress.ToString(), remoteServerPort, token)
                    .ConfigureAwait(false);

            if (sendMessageResult2.Failure)
            {
                Assert.Fail($"There was an error sending a text message to the client: {sendMessageResult2.Error}");
            }

            while (!_clientReceivedTextMessage) { }

            Assert.AreEqual(messageForServer, _messageFromClient);
            Assert.AreEqual(messageForClient, _messageFromServer);

            try
            {
                _tokenSource.Cancel();
                var runServerResult1 = runServerTask1.Result;
                if (runServerResult1.Failure)
                {
                    Assert.Fail(
                        "There was an error attempting to shutdown the server: "
                        + runServerResult1.Error);
                }
            }
            catch (AggregateException ex)
            {
                Console.WriteLine("\nException messages:");
                foreach (var ie in ex.InnerExceptions)
                {
                    Console.WriteLine($"\t{ie.GetType().Name}: {ie.Message}");
                }

                Console.WriteLine($"\nServer Shutdown Task 1 status: {runServerTask1.Status}\n");
            }
            finally
            {
                _server.CloseListenSocket();
            }

            while (!_serverListenSocketIsShutdown) { }

            try
            {
                var runServerResult2 = runServerTask2.Result;
                if (runServerResult2.Failure)
                {
                    Assert.Fail(
                        "There was an error attempting to shutdown the server: "
                        + runServerResult2.Error);
                }
            }
            catch (AggregateException ex)
            {
                Console.WriteLine("\nException messages:");
                foreach (var ie in ex.InnerExceptions)
                {
                    Console.WriteLine($"\t{ie.GetType().Name}: {ie.Message}");
                }

                Console.WriteLine($"\nServer Shutdown Task 2 status: {runServerTask2.Status}\n");
            }
            finally
            {
                _client.CloseListenSocket();
            }

            while (!_clientListenSocketIsShutdown) { }
        }

        [TestMethod]
        public async Task VerifySendFileAsync()
        {
            var remoteServerPort = 8003;
            var localPort = 8004;

            var sendFilePath = _localFilePath;
            var receiveFilePath = _remoteFilePath;
            var receiveFolderPath = _remoteFolder;

            var token = _tokenSource.Token;

            var runServerListenTask =
                Task.Run(() =>
                        _server.HandleIncomingConnectionsAsync(
                            remoteServerPort,
                            token),
                    token);

            var runClientListenTask =
                Task.Run(() =>
                        _client.HandleIncomingConnectionsAsync(
                            localPort,
                            token),
                    token);

            while (!_serverIsListening) { }
            while (!_clientIsListening) { }

            var sizeOfFileToSend = new FileInfo(sendFilePath).Length;
            FileHelper.DeleteFileIfAlreadyExists(receiveFilePath);
            Assert.IsFalse(File.Exists(receiveFilePath));

            var sendFileTask =
                Task.Run(
                    () => _client.SendFileAsync(
                                    _ipAddress.ToString(),
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

            try
            {
                _tokenSource.Cancel();
                var serverResult = runServerListenTask.Result;
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

                Console.WriteLine($"\nServer Shutdown Task status: {runServerListenTask.Status}\n");
            }
            finally
            {
                _server.CloseListenSocket();
            }

            while (!_serverListenSocketIsShutdown) { }

            try
            {
                var clientResult = runClientListenTask.Result;
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

                Console.WriteLine($"\nClient Shutdown Task status: {runClientListenTask.Status}\n");
            }
            finally
            {
                _client.CloseListenSocket();
            }

            while (!_clientListenSocketIsShutdown) { }
        }

        [TestMethod]
        public async Task VerifyGetFileAsync()
        {
            var localPort = 8005;
            var remoteServerPort = 8006;
            var getFilePath = _remoteFilePath;
            var receivedFilePath = _localFilePath;

            var token = _tokenSource.Token;

            var runServerTask1 =
                Task.Run(() =>
                    _server.HandleIncomingConnectionsAsync(
                        _ipAddress.ToString(),
                        remoteServerPort,
                        token),
                    token);

            var runServerTask2 =
                Task.Run(() =>
                    _client.HandleIncomingConnectionsAsync(
                        _ipAddress.ToString(),
                        localPort,
                        token),
                    token);

            while (!_serverIsListening) { }
            while (!_clientIsListening) { }

            FileHelper.DeleteFileIfAlreadyExists(receivedFilePath);
            Assert.IsFalse(File.Exists(receivedFilePath));

            var getFileResult =
                await _client.GetFileAsync(_ipAddress.ToString(), remoteServerPort, getFilePath, _ipAddress.ToString(), localPort, _localFolder, token)
                            .ConfigureAwait(false);

            if (getFileResult.Failure)
            {
                Assert.Fail("There was an error receiving the file from the remote server: " + getFileResult.Error);
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

            try
            {
                _tokenSource.Cancel();
                var runServerResult1 = runServerTask1.Result;
                if (runServerResult1.Failure)
                {
                    Assert.Fail(
                        "There was an error attempting to shutdown the server: "
                        + runServerResult1.Error);
                }
            }
            catch (AggregateException ex)
            {
                Console.WriteLine("\nException messages:");
                foreach (var ie in ex.InnerExceptions)
                {
                    Console.WriteLine($"\t{ie.GetType().Name}: {ie.Message}");
                }

                Console.WriteLine($"\nServer Shutdown Task 1 status: {runServerTask1.Status}\n");
            }
            finally
            {
                _server.CloseListenSocket();
            }

            while (!_serverListenSocketIsShutdown) { }

            try
            {
                var runServerResult2 = runServerTask2.Result;
                if (runServerResult2.Failure)
                {
                    Assert.Fail(
                        "There was an error attempting to shutdown the server: "
                        + runServerResult2.Error);
                }
            }
            catch (AggregateException ex)
            {
                Console.WriteLine("\nException messages:");
                foreach (var ie in ex.InnerExceptions)
                {
                    Console.WriteLine($"\t{ie.GetType().Name}: {ie.Message}");
                }

                Console.WriteLine($"\nServer Shutdown Task 2 status: {runServerTask2.Status}\n");
            }
            finally
            {
                _client.CloseListenSocket();
            }

            while (!_clientListenSocketIsShutdown) { }
        }

        [TestMethod]
        public async Task VerifyRequestTransferFolderPath()
        {
            var localPort = 8007;
            var remoteServerPort = 8008;

            _server.TransferFolderPath = _remoteFolder;
            var token = _tokenSource.Token;

            var runServerListenTask =
                Task.Run(() =>
                        _server.HandleIncomingConnectionsAsync(
                            remoteServerPort,
                            token),
                        token);

            var runClientListenTask =
                Task.Run(() =>
                        _client.HandleIncomingConnectionsAsync(
                            localPort,
                            token),
                        token);

            while (!_serverIsListening) { }
            while (!_clientIsListening) { }
            Assert.AreEqual(string.Empty, _transferFolderPath);

            var transferFolderRequest =
               await _client.RequestTransferFolderPath(
                    _ipAddress.ToString(),
                    remoteServerPort,
                    _ipAddress.ToString(),
                    localPort,
                    token).ConfigureAwait(false);

            if (transferFolderRequest.Failure)
            {
                Assert.Fail("Error sending request for transfer folder path.");
            }

            while(!_clientReceivedTransferFolderPath) { }
            Assert.AreEqual(_remoteFolder, _transferFolderPath);

            try
            {
                _tokenSource.Cancel();
                var serverResult = runServerListenTask.Result;
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

                Console.WriteLine($"\nServer Shutdown Task status: {runServerListenTask.Status}\n");
            }
            finally
            {
                _server.CloseListenSocket();
            }

            while (!_serverListenSocketIsShutdown) { }

            try
            {
                var clientResult = runClientListenTask.Result;
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

                Console.WriteLine($"\nClient Shutdown Task status: {runClientListenTask.Status}\n");
            }
            finally
            {
                _client.CloseListenSocket();
            }

            while (!_clientListenSocketIsShutdown) { }
        }

        private void HandleClientEvent(ServerEventInfo serverEventInfo)
        {

            Console.WriteLine("(client) " + serverEventInfo.Report());

            switch (serverEventInfo.EventType)
            {
                case ServerEventType.ListenOnLocalPortCompleted:
                    _clientIsListening = true;
                    break;

                case ServerEventType.ReceiveTextMessageCompleted:
                    _clientReceivedTextMessage = true;
                    _messageFromServer = serverEventInfo.TextMessage;
                    break;

                case ServerEventType.ReceiveTransferFolderResponseCompleted:
                    _transferFolderPath = serverEventInfo.RemoteFolder;
                    _clientReceivedTransferFolderPath = true;
                    break;

                case ServerEventType.ReceiveFileBytesCompleted:
                    _clientReceivedAllFileBytes = true;
                    break;

                case ServerEventType.ReceiveConfirmationMessageCompleted:
                    _clientReceivedConfirmationMessage = true;
                    break;

                case ServerEventType.ShutdownListenSocketCompleted:
                    _clientListenSocketIsShutdown = true;
                    break;

                case ServerEventType.ErrorOccurred:
                    _clientErrorOccurred = true;
                    break;
            }
        }

        private void HandleServerEvent(ServerEventInfo serverEventInfo)
        {
            Console.WriteLine("(server) " + serverEventInfo.Report());

            switch (serverEventInfo.EventType)
            {
                case ServerEventType.ListenOnLocalPortCompleted:
                    _serverIsListening = true;
                    break;

                case ServerEventType.ReceiveTextMessageCompleted:
                    _serverReceivedTextMessage = true;
                    _messageFromClient = serverEventInfo.TextMessage;
                    break;

                case ServerEventType.ReceiveFileBytesCompleted:
                    _serverReceivedAllFileBytes = true;
                    break;

                case ServerEventType.ReceiveConfirmationMessageCompleted:
                    _serverReceivedConfirmationMessage = true;
                    break;

                case ServerEventType.ShutdownListenSocketCompleted:
                    _serverListenSocketIsShutdown = true;
                    break;

                case ServerEventType.ErrorOccurred:
                    _serverErrorOccurred = true;
                    break;
            }
        }
    }
}
