using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using AaronLuna.AsyncSocketServer;
using AaronLuna.AsyncSocketServer.FileTransfers;
using AaronLuna.Common.Extensions;
using AaronLuna.Common.IO;
using AaronLuna.Common.Logging;
using AaronLuna.Common.Network;
using AaronLuna.Common.Result;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AaronLuna.AsyncSocketServerTest
{
    [TestClass]
    public partial class AsyncServerTestFixture
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

            var server = new AsyncServer(_serverSettings);
            server.EventOccurred += HandleServerEvent;
            server.SocketEventOccurred += HandleServerEvent;

            var client = new AsyncServer(_clientSettings);
            client.EventOccurred += HandleClientEvent;
            client.SocketEventOccurred += HandleClientEvent;

            await server.InitializeAsync("server").ConfigureAwait(false);
            await client.InitializeAsync("client").ConfigureAwait(false);

            var runServerTask = Task.Run(() => server.RunAsync(token), token);
            var runClientTask = Task.Run(() => client.RunAsync(token), token);

            while (!server.IsRunning) { }
            while (!client.IsRunning) { }

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

            var server = new AsyncServer(_serverSettings);
            server.EventOccurred += HandleServerEvent;
            server.SocketEventOccurred += HandleServerEvent;

            var client = new AsyncServer(_clientSettings);
            client.EventOccurred += HandleClientEvent;
            client.SocketEventOccurred += HandleClientEvent;

            await server.InitializeAsync("server").ConfigureAwait(false);
            await client.InitializeAsync("client").ConfigureAwait(false);

            var runServerTask = Task.Run(() => server.RunAsync(token), token);
            var runClientTask = Task.Run(() => client.RunAsync(token), token);

            while (!server.IsRunning) { }
            while (!client.IsRunning) { }

            Assert.IsTrue(server.Conversations.Count == 0);

            var sendMessageToServer =
                await client.SendTextMessageAsync(
                        server.MyInfo,
                        messageForServer)
                    .ConfigureAwait(false);

            if (sendMessageToServer.Failure)
            {
                Assert.Fail($"There was an error sending a text message to the server: {sendMessageToServer.Error}");
            }

            while (!_serverReceivedTextMessage) { }

            Assert.IsTrue(server.Conversations.Count > 0);
            Assert.IsTrue(server.Conversations[0].MessageCount > 0);
            Assert.AreEqual(messageForServer, server.Conversations[0].Messages[0].Text);

            var sendMessageToClient =
                await server.SendTextMessageAsync(
                        client.MyInfo,
                        messageForClient)
                    .ConfigureAwait(false);

            if (sendMessageToClient.Failure)
            {
                Assert.Fail($"There was an error sending a text message to the client: {sendMessageToClient.Error}");
            }

            while (!_clientReceivedTextMessage) { }

            Assert.IsTrue(client.Conversations.Count > 0);
            Assert.IsTrue(client.Conversations[0].MessageCount > 1);
            Assert.AreEqual(messageForClient, client.Conversations[0].Messages[1].Text);

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
            var logMessageForConsole = $"(client)\t{serverEvent}";

            var logMessageForFile =
                $"(client)\t{DateTime.Now:MM/dd/yyyy HH:mm:ss.fff}\t{serverEvent.GetLogFileEntry()}";

            if (!serverEvent.DoNotDisplayInLog)
            {
                Console.WriteLine(logMessageForConsole);
                _clientLogMessages.Add(logMessageForFile);
            }

            switch (serverEvent.EventType)
            {
                case EventType.PendingFileTransfer:
                    _clientFileTransferPending = true;
                    break;

                case EventType.ReceivedTextMessage:
                    _clientReceivedTextMessage = true;
                    break;

                case EventType.ReceivedServerInfo:
                    _remoteServerFolderPath = serverEvent.RemoteFolder;
                    _remoteServerPublicIp = serverEvent.PublicIpAddress;
                    _remoteServerLocalIp = serverEvent.LocalIpAddress;
                    _remoteServerPlatform = serverEvent.RemoteServerPlatform;
                    _clientReceivedServerInfo = true;
                    break;

                case EventType.ReceivedFileList:
                    _fileInfoList = serverEvent.RemoteServerFileList;
                    _clientReceivedFileInfoList = true;
                    break;

                case EventType.ReceivedNotificationFileDoesNotExist:
                    _clientWasNotifiedFileDoesNotExist = true;
                    break;

                case EventType.ReceiveFileBytesStarted:
                    _clientReceiveFileBytesStarted = true;
                    break;

                case EventType.ReceiveFileBytesComplete:
                    _clientReceiveFileBytesComplete = true;
                    break;

                case EventType.ReceivedRetryLimitExceeded:
                    _clientWasNotifiedRetryLimitExceeded = true;
                    break;

                case EventType.RemoteServerRejectedFileTransfer:
                    _serverRejectedFileTransfer = true;
                    break;

                case EventType.ReceivedNotificationFolderIsEmpty:
                    _clientWasNotifiedFolderIsEmpty = true;
                    break;

                case EventType.ReceivedNotificationFolderDoesNotExist:
                    _clientWasNotifiedFolderDoesNotExist = true;
                    break;

                case EventType.RemoteServerConfirmedFileTransferCompleted:
                    _serverConfirmedFileTransferComplete = true;
                    break;

                case EventType.ProcessRequestBacklogStarted:
                    _clientProcessingRequestBacklogStarted = true;
                    break;

                case EventType.ProcessRequestBacklogComplete:
                    _clientProcessingRequestBacklogComplete = true;
                    break;

                case EventType.ErrorOccurred:
                    _clientErrorOccurred = true;
                    break;
            }
        }

        void HandleServerEvent(object sender, ServerEvent serverEvent)
        {
            var logMessageForConsole = $"(server)\t{serverEvent}";

            var logMessageForFile =
                $"(server)\t{DateTime.Now:MM/dd/yyyy HH:mm:ss.fff}\t{serverEvent.GetLogFileEntry()}";

            if (!serverEvent.DoNotDisplayInLog)
            {
                Console.Write(logMessageForConsole);
                _serverLogMessages.Add(logMessageForFile);
            }

            switch (serverEvent.EventType)
            {
                case EventType.PendingFileTransfer:
                    _serverFileTransferPending = true;
                    break;

                case EventType.ReceivedTextMessage:
                    _serverReceivedTextMessage = true;
                    break;

                case EventType.StoppedSendingFileBytes:
                    _serverStoppedSendingFileBytes = true;
                    break;

                case EventType.FileTransferStalled:
                    _serverWasNotifiedFileTransferStalled = true;
                    break;

                case EventType.ReceiveFileBytesComplete:
                    _serverReceivedAllFileBytes = true;
                    break;

                case EventType.SendFileBytesStarted:
                    _serverSendFileBytesStarted = true;
                    break;

                case EventType.SendFileBytesComplete:
                    _serverSendFileBytesComplete = true;
                    break;

                case EventType.RemoteServerRejectedFileTransfer:
                    _clientRejectedFileTransfer = true;
                    break;

                case EventType.RemoteServerConfirmedFileTransferCompleted:
                    _clientConfirmedFileTransferComplete = true;
                    break;

                case EventType.ProcessRequestBacklogStarted:
                    _serverProcessingRequestBacklogStarted = true;
                    break;

                case EventType.ProcessRequestBacklogComplete:
                    _serverProcessingRequestBacklogComplete = true;
                    break;

                case EventType.ErrorOccurred:
                    _serverErrorOccurred = true;
                    break;
            }
        }

        async Task ShutdownServerAsync(AsyncServer server, Task<Result> runServerTask)
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