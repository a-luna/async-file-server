using System.Threading;

namespace AaronLuna.AsyncSocketServerTest
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading.Tasks;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    using Common.IO;
    using Common.Logging;
    using Common.Result;

    using AsyncSocketServer;
    using TestClasses;

    public partial class AsyncServerTestFixture
    {
        List<string> _client2LogMessages;
        List<string> _client3LogMessages;
        List<string> _client4LogMessages;

        bool _client2ReceivedServerInfo;
        bool _client2WasNotifiedFileDoesNotExist;
        bool _client3ReceivedFileInfoList;
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

            var testServer = new SpecialAsyncServer(_serverSettings);
            testServer.EventOccurred += HandleServerEvent;
            testServer.SocketEventOccurred += HandleServerEvent;

            var testClient = new SpecialAsyncServer(_clientSettings);
            testClient.EventOccurred += HandleClientEvent;
            testClient.SocketEventOccurred += HandleClientEvent;

            var client2 = new AsyncServer(client2Settings);
            client2.EventOccurred += HandleClient2Event;
            client2.SocketEventOccurred += HandleClient2Event;

            var client3 = new AsyncServer(client3Settings);
            client3.EventOccurred += HandleClient3Event;
            client3.SocketEventOccurred += HandleClient3Event;

            var client4 = new AsyncServer(client4Settings);
            client4.EventOccurred += HandleClient4Event;
            client4.SocketEventOccurred += HandleClient4Event;

            var testClientState = new ServerState(testClient);
            var testServerState = new ServerState(testServer);
            var client2State = new ServerState(client2);
            var client3State = new ServerState(client3);
            var client4State = new ServerState(client4);

            await testServer.InitializeAsync("test server").ConfigureAwait(false);
            await testClient.InitializeAsync("test client").ConfigureAwait(false);
            await client2.InitializeAsync("client2").ConfigureAwait(false);
            await client3.InitializeAsync("client3").ConfigureAwait(false);
            await client4.InitializeAsync("client4").ConfigureAwait(false);

            var runTestServerTask = Task.Run(() => testServer.RunAsync(token), token);
            var runTestClientTask = Task.Run(() => testClient.RunAsync(token), token);
            var runClient2Task = Task.Run(() => client2.RunAsync(token), token);
            var runClient3Task = Task.Run(() => client3.RunAsync(token), token);
            var runClient4Task = Task.Run(() => client4.RunAsync(token), token);

            while (!testServer.IsRunning) { }
            while (!testClient.IsRunning) { }
            while (!client2.IsRunning) { }
            while (!client3.IsRunning) { }
            while (!client4.IsRunning) { }

            Assert.IsTrue(File.Exists(_remoteFilePath));
            var sentFileSize = new FileInfo(_remoteFilePath).Length;

            FileHelper.DeleteFileIfAlreadyExists(_localFilePath, 3);
            Assert.IsFalse(File.Exists(_localFilePath));

            // Request file transfer which will be mocked to simulate a long-running transfer
            var getFileResult =
                await testClient.GetFileAsync(
                    testServer.MyInfo,
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

            testServer.UseMockTimeFileSender(fileTransferDuration);
            testClient.UseMockTimeFileReceiver(fileTransferDuration);

            var receiveFileTask = Task.Run(() => testClient.AcceptInboundFileTransferAsync(pendingFiletransfer), token);

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

            Thread.Sleep(900);

            var serverInfoRequest = await
                client2.RequestServerInfoAsync(
                        testServer.MyInfo.LocalIpAddress,
                        testServer.MyInfo.PortNumber)
                    .ConfigureAwait(false);

            Thread.Sleep(150);

            var fileListRequest1 =
                await client3.RequestFileListAsync(testServer.MyInfo).ConfigureAwait(false);

            Thread.Sleep(150);

            var textMessage1 =
                await client4.SendTextMessageAsync(testServer.MyInfo, "this too shall pend");

            Thread.Sleep(150);

            var sendFile1 = await
                client2.SendFileAsync(
                    testClient.MyInfo,
                    "fake.exe",
                    46692,
                    _testFilesFolder,
                    _localFolder);

            Thread.Sleep(150);

            var sendFile2 = await
                client3.SendFileAsync(
                    testServer.MyInfo,
                    "fake.exe",
                    46692,
                    _testFilesFolder,
                    _remoteFolder);

            Thread.Sleep(150);

            var fileListRequest2 =
                await client4.RequestFileListAsync(testClient.MyInfo).ConfigureAwait(false);

            Thread.Sleep(150);

            var getFileResult2 =
                await client2.GetFileAsync(
                    testClient.MyInfo,
                    "private_key.txt",
                    4559,
                    _remoteFolder,
                    _localFolder).ConfigureAwait(false);

            Thread.Sleep(150);

            var textMessage2 =
                await client3.SendTextMessageAsync(testClient.MyInfo, "the pending will never end");

            Thread.Sleep(150);

            var textMessage3 =
                await client4.SendTextMessageAsync(testServer.MyInfo, "pending all over your face");

            Thread.Sleep(900);

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

            while (!_serverProcessingRequestBacklogStarted) { }
            while (!_clientProcessingRequestBacklogStarted) { }

            while (!_client2WasNotifiedFileDoesNotExist) { }
            while (!_client2ReceivedServerInfo) { }
            while (!_client3ReceivedFileInfoList) { }
            while (!_client4WasNotifiedFolderIsEmpty) { }
            while (!_clientReceivedTextMessage) { }
            while (!_serverReceivedTextMessage) { }

            while (!_serverProcessingRequestBacklogComplete) { }
            while (!_clientProcessingRequestBacklogComplete) { }

            Assert.AreEqual(1, testClientState.PendingFileTransferIds.Count);
            Assert.AreEqual(1, testServerState.PendingFileTransferIds.Count);

            // Check the status of all requests on all server/client instances

            var testServerString = testServer.ToString();
            var testServerEndPoint = $"{testServer.MyInfo.LocalIpAddress}:{testServer.MyInfo.PortNumber}";

            Assert.IsTrue(testServerString.Contains($"test server [{testServerEndPoint}]"));
            Assert.IsTrue(testServerString.Contains("[11 Requests (8 Rx, 3 Tx)]"));
            Assert.IsTrue(testServerString.Contains("[2 File Transfers (0 Rx, 1 Tx, 1 Pending)]"));
            Assert.IsTrue(testServerString.Contains("[2 Messages (1 conversations)]"));

            Assert.IsFalse(testServerState.PendingRequestInQueue);
            Assert.AreEqual(11, testServerState.RequestIds.Count);
            Assert.IsTrue(testServerState.FileTransferPending);
            Assert.AreEqual(2, testServerState.FileTransferIds.Count);
            Assert.IsFalse(testServerState.NoTextSessions);
            Assert.AreEqual(1, testServerState.TextSessionIds.Count);

            var textSessionId1 = testServerState.TextSessionIds[0];
            var textSession1 = testServer.GetConversationById(textSessionId1).Value;
            var messageCount1 = textSession1.MessageCount;

            Assert.AreEqual(2, messageCount1);

            var testClientString = testClient.ToString();
            var testClientEndPoint = $"{testClient.MyInfo.LocalIpAddress}:{testClient.MyInfo.PortNumber}";

            Assert.IsTrue(testClientString.Contains($"test client [{testClientEndPoint}]"));
            //Assert.IsTrue(testClientString.Contains("[10 Requests (5 Rx, 5 Tx)]"));
            Assert.IsTrue(testClientString.Contains("[2 File Transfers (1 Rx, 0 Tx, 1 Pending)]"));
            Assert.IsTrue(testClientString.Contains("[1 Messages (1 conversations)]"));

            Assert.IsFalse(testClientState.PendingRequestInQueue);
            //Assert.AreEqual(10, testClientState.RequestIds.Count);
            Assert.IsTrue(testClientState.FileTransferPending);
            Assert.AreEqual(2, testClientState.FileTransferIds.Count);
            Assert.IsFalse(testClientState.NoTextSessions);
            Assert.AreEqual(1, testClientState.TextSessionIds.Count);

            var textSessionId2 = testClientState.TextSessionIds[0];
            var textSession2 = testClient.GetConversationById(textSessionId2).Value;
            var messageCount2 = textSession2.MessageCount;

            Assert.AreEqual(1, messageCount2);

            var client2String = client2.ToString();
            var client2EndPoint = $"{client2.MyInfo.LocalIpAddress}:{client2.MyInfo.PortNumber}";

            Assert.IsTrue(client2String.Contains($"client2 [{client2EndPoint}]"));
            Assert.IsTrue(client2String.Contains("[5 Requests (2 Rx, 3 Tx)]"));
            Assert.IsTrue(client2String.Contains("[2 File Transfers (0 Rx, 0 Tx, 1 Pending, 1 Rejected)]"));
            Assert.IsTrue(client2String.Contains("[0 Messages (0 conversations)]"));

            Assert.IsFalse(client2State.PendingRequestInQueue);
            Assert.AreEqual(5, client2State.RequestIds.Count);
            Assert.IsFalse(client2State.FileTransferPending);
            Assert.AreEqual(2, client2State.FileTransferIds.Count);
            Assert.IsTrue(client2State.NoTextSessions);
            Assert.AreEqual(0, client2State.TextSessionIds.Count);

            var client3String = client3.ToString();
            var client3EndPoint = $"{client3.MyInfo.LocalIpAddress}:{client3.MyInfo.PortNumber}";

            Assert.IsTrue(client3String.Contains($"client3 [{client3EndPoint}]"));
            Assert.IsTrue(client3String.Contains("[4 Requests (1 Rx, 3 Tx)]"));
            Assert.IsTrue(client3String.Contains("[1 File Transfers (0 Rx, 0 Tx, 1 Pending)]"));
            Assert.IsTrue(client3String.Contains("[1 Messages (1 conversations)]"));

            Assert.IsFalse(client3State.PendingRequestInQueue);
            Assert.AreEqual(4, client3State.RequestIds.Count);
            Assert.IsFalse(client3State.FileTransferPending);
            Assert.AreEqual(1, client3State.FileTransferIds.Count);
            Assert.IsFalse(client3State.NoTextSessions);
            Assert.AreEqual(1, client3State.TextSessionIds.Count);

            var textSessionId3 = client3State.TextSessionIds[0];
            var textSession3 = client3.GetConversationById(textSessionId3).Value;
            var messageCount3 = textSession3.MessageCount;

            Assert.AreEqual(1, messageCount3);

            var client4String = client4.ToString();
            var client4EndPoint = $"{client4.MyInfo.LocalIpAddress}:{client4.MyInfo.PortNumber}";

            Assert.IsTrue(client4String.Contains($"client4 [{client4EndPoint}]"));
            Assert.IsTrue(client4String.Contains("[4 Requests (1 Rx, 3 Tx)]"));
            Assert.IsTrue(client4String.Contains("[0 File Transfers]"));
            Assert.IsTrue(client4String.Contains("[2 Messages (1 conversations)]"));

            Assert.IsFalse(client4State.PendingRequestInQueue);
            Assert.AreEqual(4, client4State.RequestIds.Count);
            Assert.IsFalse(client4State.FileTransferPending);
            Assert.AreEqual(0, client4State.FileTransferIds.Count);
            Assert.IsFalse(client4State.NoTextSessions);
            Assert.AreEqual(1, client4State.TextSessionIds.Count);

            var textSessionId4 = client4State.TextSessionIds[0];
            var textSession4 = client4.GetConversationById(textSessionId4).Value;
            var messageCount4 = textSession4.MessageCount;

            Assert.AreEqual(2, messageCount4);

            var fileTransferEventLog = testServer.GetEventLogForFileTransfer(1, LogLevel.Info);
            var requestEventLog = testServer.GetEventLogForRequest(1);
            var allEvents = testServer.GetCompleteEventLog(LogLevel.Trace);

            Assert.AreEqual(1, fileTransferEventLog.Count);
            Assert.AreEqual(0, requestEventLog.Count);
            Assert.IsTrue(allEvents.Count >= 100);

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
            var logMessageForConsole = $"(client2)\t{serverEvent}";

            var logMessageForFile =
                $"(client2)\t{DateTime.Now:MM/dd/yyyy HH:mm:ss.fff}\t{serverEvent.GetLogFileEntry()}";

            if (!serverEvent.DoNotDisplayInLog)
            {
                Console.WriteLine(logMessageForConsole);
                _client2LogMessages.Add(logMessageForFile);
            }

            switch (serverEvent.EventType)
            {
                case EventType.ReceivedServerInfo:
                    _client2ReceivedServerInfo = true;
                    break;

                case EventType.ReceivedNotificationFileDoesNotExist:
                    _client2WasNotifiedFileDoesNotExist = true;
                    break;
            }
        }

        void HandleClient3Event(object sender, ServerEvent serverEvent)
        {
            var logMessageForConsole = $"(client3)\t{serverEvent}";

            var logMessageForFile =
                $"(client3)\t{DateTime.Now:MM/dd/yyyy HH:mm:ss.fff}\t{serverEvent.GetLogFileEntry()}";

            if (!serverEvent.DoNotDisplayInLog)
            {
                Console.WriteLine(logMessageForConsole);
                _client3LogMessages.Add(logMessageForFile);
            }

            switch (serverEvent.EventType)
            {
                case EventType.ReceivedFileList:
                    _client3ReceivedFileInfoList = true;
                    break;
            }
        }

        void HandleClient4Event(object sender, ServerEvent serverEvent)
        {
            var logMessageForConsole = $"(client4)\t{serverEvent}";

            var logMessageForFile =
                $"(client4)\t{DateTime.Now:MM/dd/yyyy HH:mm:ss.fff}\t{serverEvent.GetLogFileEntry()}";

            if (!serverEvent.DoNotDisplayInLog)
            {
                Console.WriteLine(logMessageForConsole);
                _client4LogMessages.Add(logMessageForFile);
            }

            switch (serverEvent.EventType)
            {
                case EventType.ReceivedNotificationFolderIsEmpty:
                    _client4WasNotifiedFolderIsEmpty = true;
                    break;
            }
        }
    }
}
