namespace AaronLuna.AsyncFileServerTest
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading.Tasks;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    using Common.IO;
    using Common.Logging;
    using Common.Result;

    using AsyncFileServer.Model;
    using AsyncFileServer.Controller;
    using TestClasses;

    public partial class AsyncFileServerTestFixture
    {
        List<string> _client2LogMessages;
        List<string> _client3LogMessages;
        List<string> _client4LogMessages;

        bool _client2ReceivedServerInfo;
        bool _client2WasNotifiedFileDoesNotExist;
        bool _client2ReceivedFileTransferComplete;
        bool _client3ReceivedFileInfoList;
        bool _client3ReceivedFileTransferComplete;
        bool _client4WasNotifiedFolderIsEmpty;

        [TestMethod]
        public async Task VerifyProcessRequestBacklog()
        {
            var testClientLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyProcessRequestBacklog_testClient.log";
            var testServerLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyProcessRequestBacklog_testServer.log";

            var client2LogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyProcessRequestBacklog_client2.log";
            var client3LogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyProcessRequestBacklog_client3.log";
            var client4LogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyProcessRequestBacklog_client4.log";

            _client2LogMessages = new List<string>();
            _client3LogMessages = new List<string>();
            _client4LogMessages = new List<string>();

            _clientSettings.LocalServerPortNumber = 8027;
            _serverSettings.LocalServerPortNumber = 8028;

            var client2Settings = new ServerSettings
            {
                LocalServerPortNumber = 8029,
                LocalServerFolderPath = _localFolder,
                LocalNetworkCidrIp = _cidrIp,
                SocketSettings = _socketSettings,
                TransferUpdateInterval = 0.10f,
                FileTransferStalledTimeout = TimeSpan.FromSeconds(5),
                TransferRetryLimit = 2,
                RetryLimitLockout = TimeSpan.FromSeconds(3),
                LogLevel = LogLevel.Info
            };

            var client3Settings = new ServerSettings
            {
                LocalServerPortNumber = 8030,
                LocalServerFolderPath = _localFolder,
                LocalNetworkCidrIp = _cidrIp,
                SocketSettings = _socketSettings,
                TransferUpdateInterval = 0.10f,
                FileTransferStalledTimeout = TimeSpan.FromSeconds(5),
                TransferRetryLimit = 2,
                RetryLimitLockout = TimeSpan.FromSeconds(3),
                LogLevel = LogLevel.Info
            };

            var client4Settings = new ServerSettings
            {
                LocalServerPortNumber = 8031,
                LocalServerFolderPath = _localFolder,
                LocalNetworkCidrIp = _cidrIp,
                SocketSettings = _socketSettings,
                TransferUpdateInterval = 0.10f,
                FileTransferStalledTimeout = TimeSpan.FromSeconds(5),
                TransferRetryLimit = 2,
                RetryLimitLockout = TimeSpan.FromSeconds(3),
                LogLevel = LogLevel.Info
            };
            
            var token = _cts.Token;

            var testServer = new AsyncFileServerTest("test server", _serverSettings);
            testServer.EventOccurred += HandleServerEvent;
            testServer.SocketEventOccurred += HandleServerEvent;
            testServer.TestEventOccurred += HandleServerEvent;
            testServer.TestSocketEventOccurred += HandleServerEvent;

            var testClient = new AsyncFileServerTest("test client", _clientSettings);
            testClient.EventOccurred += HandleClientEvent;
            testClient.SocketEventOccurred += HandleClientEvent;
            testClient.TestEventOccurred += HandleClientEvent;
            testClient.TestSocketEventOccurred += HandleClientEvent;

            var client2 = new AsyncFileServer("client2", client2Settings);
            client2.EventOccurred += HandleClient2Event;
            client2.SocketEventOccurred += HandleClient2Event;

            var client3 = new AsyncFileServer("client3", client3Settings);
            client3.EventOccurred += HandleClient3Event;
            client3.SocketEventOccurred += HandleClient3Event;

            var client4 = new AsyncFileServer("client4", client4Settings);
            client4.EventOccurred += HandleClient4Event;
            client4.SocketEventOccurred += HandleClient4Event;

            var testClientState = new ServerState(testClient);
            var testServerState = new ServerState(testServer);
            var client2State = new ServerState(client2);
            var client3State = new ServerState(client3);
            var client4State = new ServerState(client4);

            await testServer.InitializeAsync(_serverSettings).ConfigureAwait(false);
            await testClient.InitializeAsync(_clientSettings).ConfigureAwait(false);
            await client2.InitializeAsync(client2Settings).ConfigureAwait(false);
            await client3.InitializeAsync(client3Settings).ConfigureAwait(false);
            await client4.InitializeAsync(client4Settings).ConfigureAwait(false);

            var runTestServerTask = Task.Run(() => testServer.RunAsync(token), token);
            var runTestClientTask = Task.Run(() => testClient.RunAsync(token), token);
            var runClient2Task = Task.Run(() => client2.RunAsync(token), token);
            var runClient3Task = Task.Run(() => client3.RunAsync(token), token);
            var runClient4Task = Task.Run(() => client4.RunAsync(token), token);

            while (!testServer.IsListening) { }
            while (!testClient.IsListening) { }
            while (!client2.IsListening) { }
            while (!client3.IsListening) { }
            while (!client4.IsListening) { }
            
            Assert.IsTrue(File.Exists(_remoteFilePath));
            var sentFileSize = new FileInfo(_remoteFilePath).Length;

            FileHelper.DeleteFileIfAlreadyExists(_localFilePath, 3);
            Assert.IsFalse(File.Exists(_localFilePath));

            // Request file transfer which will be mocked to simulate a long-running transfer
            var getFileResult =
                await testClient.GetFileAsync(
                    testServer.MyInfo.LocalIpAddress,
                    testServer.MyInfo.PortNumber,
                    testServer.MyInfo.Name,
                    FileName,
                    sentFileSize,
                    _remoteFolder,
                    _localFolder).ConfigureAwait(false);

            if (getFileResult.Failure)
            {
                var getFileError = "There was an error requesting the file from the remote server: " + getFileResult.Error;
                Assert.Fail(getFileError);
            }
            
            while (!_clientFileTransferPending) { }

            // Before accepting the file transfer, change both server and client instances of the
            // file transfer controller object to use the mock transfer controllers

            var pendingId = testClientState.PendingFileTransferIds[0];
            var pendingFiletransfer = testClient.GetFileTransferById(pendingId).Value;

            var fileTransferDuration = TimeSpan.FromSeconds(3);

            var useMock1 =
                testServer.UseMockFileTransferSendController(
                    pendingFiletransfer.TransferResponseCode,
                    5,
                    fileTransferDuration);

            if (useMock1.Failure)
            {
                Assert.Fail(useMock1.Error);
            }

            var useMock2 =
                testClient.UseMockFileTransferReceiveController(
                    pendingFiletransfer.TransferResponseCode,
                    5,
                    fileTransferDuration);

            if (useMock2.Failure)
            {
                Assert.Fail(useMock2.Error);
            }

            var mockFileTransfer = testClient.GetFileTransferById(pendingId).Value;
            var receiveFileTask = Task.Run(() => testClient.AcceptInboundFileTransferAsync(mockFileTransfer), token);

            while (!_serverSendFileBytesStarted) { }
            while (!_clientReceiveFileBytesStarted) { }

            _clientFileTransferPending = false;

            var clientFilePath = Path.Combine(_localFolder, "fake.exe");
            var serverFilePath = Path.Combine(_remoteFolder, "fake.exe");

            FileHelper.DeleteFileIfAlreadyExists(clientFilePath, 3);
            Assert.IsFalse(File.Exists(clientFilePath));

            FileHelper.DeleteFileIfAlreadyExists(serverFilePath, 3);
            Assert.IsFalse(File.Exists(serverFilePath));

            // Both testClient and testServer will be "busy" for the length of time defined in fileTransferDuration
            // Now, the remaining client instances will send requests to both servers which should be placed in
            // the queue and processed after the file transfer has completed.

            await Task.Delay(700, token);

            var serverInfoRequest = await
                client2.RequestServerInfoAsync(
                        testServer.MyInfo.LocalIpAddress,
                        testServer.MyInfo.PortNumber)
                    .ConfigureAwait(false);

            await Task.Delay(100, token);

            var fileListRequest1 = await
                client3.RequestFileListAsync(
                        testClient.MyInfo.LocalIpAddress,
                        testClient.MyInfo.PortNumber,
                        _testFilesFolder)
                    .ConfigureAwait(false);

            await Task.Delay(100, token);

            var textMessage1 = await
                client4.SendTextMessageAsync(
                    "this too shall pend",
                    testServer.MyInfo.LocalIpAddress,
                    testServer.MyInfo.PortNumber);

            await Task.Delay(100, token);

            var sendFile1 = await
                client2.SendFileAsync(
                    testClient.MyInfo.LocalIpAddress,
                    testClient.MyInfo.PortNumber,
                    testClient.MyInfo.Name,
                    "fake.exe",
                    46692,
                    _testFilesFolder,
                    _localFolder);

            await Task.Delay(100, token);

            var sendFile2 = await
                client3.SendFileAsync(
                    testServer.MyInfo.LocalIpAddress,
                    testServer.MyInfo.PortNumber,
                    testServer.MyInfo.Name,
                    "fake.exe",
                    46692,
                    _testFilesFolder,
                    _remoteFolder);

            await Task.Delay(100, token);

            var fileListRequest2 = await
                client4.RequestFileListAsync(
                        testServer.MyInfo.LocalIpAddress,
                        testServer.MyInfo.PortNumber,
                        _emptyFolder)
                    .ConfigureAwait(false);

            await Task.Delay(100, token);

            var getFileResult2 =
                await client2.GetFileAsync(
                    testClient.MyInfo.LocalIpAddress,
                    testClient.MyInfo.PortNumber,
                    testClient.MyInfo.Name,
                    "private_key.txt",
                    4559,
                    _remoteFolder,
                    _localFolder).ConfigureAwait(false);

            await Task.Delay(100, token);

            var textMessage2 = await
                client3.SendTextMessageAsync(
                    "the pending will never end",
                    testClient.MyInfo.LocalIpAddress,
                    testClient.MyInfo.PortNumber);

            await Task.Delay(100, token);

            var textMessage3 = await
                client4.SendTextMessageAsync(
                    "pending all over your face",
                    testServer.MyInfo.LocalIpAddress,
                    testServer.MyInfo.PortNumber);

            if (Result.Combine(
                serverInfoRequest,
                fileListRequest1,
                textMessage1,
                sendFile1,
                sendFile2,
                fileListRequest2,
                getFileResult2,
                textMessage2,
                textMessage3).Failure)
            {
                Assert.Fail("There was an error with one of the clients sending requests to the test instances");
            }

            while (!_serverSendFileBytesComplete) { }
            while (!_clientReceiveFileBytesComplete) { }

            var receivedFile = await receiveFileTask;
            if (receivedFile.Failure)
            {
                Assert.Fail("Error receiving file.");
            }

            //while (!_serverProcessingRequestBacklogStarted) { }
            while (!_clientProcessingRequestBacklogStarted) { }

            while (!_client2WasNotifiedFileDoesNotExist) { }
            while (!_client2ReceivedServerInfo) { }
            while (!_client3ReceivedFileInfoList) { }
            while (!_client4WasNotifiedFolderIsEmpty) { }
            while (!_clientReceivedTextMessage) { }
            while (!_serverReceivedTextMessage) { }

            //while (!_serverProcessingRequestBacklogComplete) { }
            while (!_clientProcessingRequestBacklogComplete) { }

            Assert.AreEqual(1, testClientState.PendingFileTransferIds.Count);
            Assert.AreEqual(1, testServerState.PendingFileTransferIds.Count);

            // Check the status of all requests on all server/client instances

            var testServerString = testServer.ToString();
            Assert.IsTrue(testServerString.Contains("test server [172.20.10.10:8028]"));
            Assert.IsTrue(testServerString.Contains("[Requests In: 8 Out: 3]"));
            Assert.IsTrue(testServerString.Contains("[Transfers In: 1 Out: 1]"));
            Assert.IsTrue(testServerString.Contains("[Total Messages: 2/1 Sessions]"));

            Assert.IsFalse(testServerState.PendingRequestInQueue);
            Assert.AreEqual(11, testServerState.RequestIds.Count);
            Assert.IsTrue(testServerState.FileTransferPending);
            Assert.AreEqual(2, testServerState.FileTransferIds.Count);
            Assert.IsFalse(testServerState.NoTextSessions);
            Assert.AreEqual(1, testServerState.TextSessionIds.Count);

            var textSessionId1 = testServerState.TextSessionIds[0];
            var textSession1 = testServer.GetTextSessionById(textSessionId1).Value;
            var messageCount1 = textSession1.MessageCount;

            Assert.AreEqual(2, messageCount1);

            var testClientString = testClient.ToString();
            Assert.IsTrue(testClientString.Contains("test client [172.20.10.10:8027]"));
            Assert.IsTrue(testClientString.Contains("[Requests In: 5 Out: 5]"));
            Assert.IsTrue(testClientString.Contains("[Transfers In: 2 Out: 0]"));
            Assert.IsTrue(testClientString.Contains("[Total Messages: 1/1 Sessions]"));

            Assert.IsFalse(testClientState.PendingRequestInQueue);
            Assert.AreEqual(10, testClientState.RequestIds.Count);
            Assert.IsTrue(testClientState.FileTransferPending);
            Assert.AreEqual(2, testClientState.FileTransferIds.Count);
            Assert.IsFalse(testClientState.NoTextSessions);
            Assert.AreEqual(1, testClientState.TextSessionIds.Count);

            var textSessionId2 = testClientState.TextSessionIds[0];
            var textSession2 = testClient.GetTextSessionById(textSessionId2).Value;
            var messageCount2 = textSession2.MessageCount;

            Assert.AreEqual(1, messageCount2);

            var client2String = client2.ToString();
            Assert.IsTrue(client2String.Contains("client2 [172.20.10.10:8029]"));
            Assert.IsTrue(client2String.Contains("[Requests In: 2 Out: 3]"));
            Assert.IsTrue(client2String.Contains("[Transfers In: 1 Out: 1]"));
            Assert.IsTrue(client2String.Contains("[Total Messages: 0/0 Sessions]"));

            Assert.IsFalse(client2State.PendingRequestInQueue);
            Assert.AreEqual(5, client2State.RequestIds.Count);
            Assert.IsFalse(client2State.FileTransferPending);
            Assert.AreEqual(2, client2State.FileTransferIds.Count);
            Assert.IsTrue(client2State.NoTextSessions);
            Assert.AreEqual(0, client2State.TextSessionIds.Count);

            var client3String = client3.ToString();
            Assert.IsTrue(client3String.Contains("client3 [172.20.10.10:8030]"));
            Assert.IsTrue(client3String.Contains("[Requests In: 1 Out: 3]"));
            Assert.IsTrue(client3String.Contains("[Transfers In: 0 Out: 1]"));
            Assert.IsTrue(client3String.Contains("[Total Messages: 1/1 Sessions]"));

            Assert.IsFalse(client3State.PendingRequestInQueue);
            Assert.AreEqual(4, client3State.RequestIds.Count);
            Assert.IsFalse(client3State.FileTransferPending);
            Assert.AreEqual(1, client3State.FileTransferIds.Count);
            Assert.IsFalse(client3State.NoTextSessions);
            Assert.AreEqual(1, client3State.TextSessionIds.Count);

            var textSessionId3 = client3State.TextSessionIds[0];
            var textSession3 = client3.GetTextSessionById(textSessionId3).Value;
            var messageCount3 = textSession3.MessageCount;

            Assert.AreEqual(1, messageCount3);

            var client4String = client4.ToString();
            Assert.IsTrue(client4String.Contains("client4 [172.20.10.10:8031]"));
            Assert.IsTrue(client4String.Contains("[Requests In: 1 Out: 3]"));
            Assert.IsTrue(client4String.Contains("[Transfers In: 0 Out: 0]"));
            Assert.IsTrue(client4String.Contains("[Total Messages: 2/1 Sessions]"));

            Assert.IsFalse(client4State.PendingRequestInQueue);
            Assert.AreEqual(4, client4State.RequestIds.Count);
            Assert.IsFalse(client4State.FileTransferPending);
            Assert.AreEqual(0, client4State.FileTransferIds.Count);
            Assert.IsFalse(client4State.NoTextSessions);
            Assert.AreEqual(1, client4State.TextSessionIds.Count);

            var textSessionId4 = client4State.TextSessionIds[0];
            var textSession4 = client4.GetTextSessionById(textSessionId4).Value;
            var messageCount4 = textSession4.MessageCount;
            
            Assert.AreEqual(2, messageCount4);

            // Cleanup all server instances
            // Cleanup all server instances
            // Cleanup all server instances

            await ShutdownServerAsync(client2, runClient2Task);
            await ShutdownServerAsync(client3, runClient3Task);
            await ShutdownServerAsync(client4, runClient4Task);
            await ShutdownServerAsync(testServer, runTestServerTask);
            await ShutdownServerAsync(testClient, runTestClientTask);

            if (_generateLogFiles)
            {
                File.AppendAllLines(testClientLogFilePath, _clientLogMessages);
                File.AppendAllLines(testServerLogFilePath, _serverLogMessages);
                File.AppendAllLines(client2LogFilePath, _client2LogMessages);
                File.AppendAllLines(client3LogFilePath, _client3LogMessages);
                File.AppendAllLines(client4LogFilePath, _client4LogMessages);
            }
        }

        void HandleClient2Event(object sender, ServerEvent serverEvent)
        {
            var logMessageForConsole =
                $"(client2)\t{DateTime.Now:MM/dd/yyyy HH:mm:ss.fff}\t{serverEvent}";

            var logMessageForFile =
                $"(client2)\t{DateTime.Now:MM/dd/yyyy HH:mm:ss.fff}\t{serverEvent.GetLogFileEntry()}";

            Console.WriteLine(logMessageForConsole);
            _client2LogMessages.Add(logMessageForFile);

            switch (serverEvent.EventType)
            {
                case ServerEventType.ReceivedServerInfo:
                    _client2ReceivedServerInfo = true;
                    break;

                case ServerEventType.ReceivedNotificationFileDoesNotExist:
                    _client2WasNotifiedFileDoesNotExist = true;
                    break;

                case ServerEventType.RemoteServerConfirmedFileTransferCompleted:
                    _client2ReceivedFileTransferComplete = true;
                    break;
            }
        }

        void HandleClient3Event(object sender, ServerEvent serverEvent)
        {
            var logMessageForConsole =
                $"(client3)\t{DateTime.Now:MM/dd/yyyy HH:mm:ss.fff}\t{serverEvent}";

            var logMessageForFile =
                $"(client3)\t{DateTime.Now:MM/dd/yyyy HH:mm:ss.fff}\t{serverEvent.GetLogFileEntry()}";

            Console.WriteLine(logMessageForConsole);
            _client3LogMessages.Add(logMessageForFile);

            switch (serverEvent.EventType)
            {
                case ServerEventType.ReceivedFileList:
                    _client3ReceivedFileInfoList = true;
                    break;

                case ServerEventType.RemoteServerConfirmedFileTransferCompleted:
                    _client3ReceivedFileTransferComplete = true;
                    break;
            }
        }

        void HandleClient4Event(object sender, ServerEvent serverEvent)
        {
            var logMessageForConsole =
                $"(client4)\t{DateTime.Now:MM/dd/yyyy HH:mm:ss.fff}\t{serverEvent}";

            var logMessageForFile =
                $"(client4)\t{DateTime.Now:MM/dd/yyyy HH:mm:ss.fff}\t{serverEvent.GetLogFileEntry()}";

            Console.WriteLine(logMessageForConsole);
            _client4LogMessages.Add(logMessageForFile);

            switch (serverEvent.EventType)
            {
                case ServerEventType.ReceivedNotificationFolderIsEmpty:
                    _client4WasNotifiedFolderIsEmpty = true;
                    break;
            }
        }
    }
    }
