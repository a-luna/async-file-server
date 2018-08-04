namespace AaronLuna.AsyncFileServerTest
{
    using System;
    using System.IO;
    using System.Threading.Tasks;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    using Common.IO;
    using Common.Logging;

    using AsyncFileServer.Controller;
    using TestClasses;

    public partial class AsyncFileServerTestFixture
    {
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

            while (!_clientFileTransferPending) { }

            var pendingFileTransferId = _clientState.PendingFileTransferIds[0];
            var pendingFileTransfer = _client.GetFileTransferById(pendingFileTransferId).Value;

            var transferResult = await _client.AcceptInboundFileTransferAsync(pendingFileTransfer);
            if (transferResult.Failure)
            {
                Assert.Fail("There was an error receiving the file from the remote server: " + transferResult.Error);
            }

            while (!_clientReceiveFileBytesComplete)
            {
                if (_clientErrorOccurred)
                {
                    Assert.Fail("File transfer failed");
                }
            }

            while (!_clientConfirmedFileTransferComplete) { }

            Assert.IsTrue(File.Exists(receivedFilePath));
            Assert.AreEqual(FileName, Path.GetFileName(receivedFilePath));

            var receivedFileSize = new FileInfo(receivedFilePath).Length;
            Assert.AreEqual(sentFileSize, receivedFileSize);

            await ShutdownServerAsync(_client, _runClientTask);
            await ShutdownServerAsync(_server, _runServerTask);

            if (_generateLogFiles)
            {
                File.AppendAllLines(_clientLogFilePath, _clientLogMessages);
                File.AppendAllLines(_serverLogFilePath, _serverLogMessages);
            }
        }

        [TestMethod]
        public async Task VerifyGetFileAfterStalledFileTransfer()
        {
            var clientLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyGetFileAfterStalledFileTransfer_client.log";
            var serverLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyGetFileAfterStalledFileTransfer_server.log";

            _clientSettings.LocalServerPortNumber = 8007;
            _serverSettings.LocalServerPortNumber = 8008;

            var token = _cts.Token;

            var testServer = new AsyncFileServerTest("test server", _serverSettings);
            testServer.EventOccurred += HandleServerEvent;
            testServer.SocketEventOccurred += HandleServerEvent;
            testServer.TestEventOccurred += HandleServerEvent;
            testServer.TestSocketEventOccurred += HandleServerEvent;

            var client = new AsyncFileServer("client", _clientSettings);
            client.EventOccurred += HandleClientEvent;
            client.SocketEventOccurred += HandleClientEvent;

            var clientState = new ServerState(client);

            await testServer.InitializeAsync(_serverSettings).ConfigureAwait(false);
            await client.InitializeAsync(_clientSettings).ConfigureAwait(false);

            var runServerTask = Task.Run(() => testServer.RunAsync(token), token);
            var runClientTask = Task.Run(() => client.RunAsync(token), token);

            while (!testServer.IsListening) { }
            while (!client.IsListening) { }

            Assert.IsTrue(File.Exists(_remoteFilePath));
            var sentFileSize = new FileInfo(_remoteFilePath).Length;

            FileHelper.DeleteFileIfAlreadyExists(_localFilePath, 3);
            Assert.IsFalse(File.Exists(_localFilePath));

            // 1st transfer attempt
            var getFileResult =
                await client.GetFileAsync(
                    _localIp,
                    _serverSettings.LocalServerPortNumber,
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

            // Wait for 1st transfer attempt
            while (!_clientFileTransferPending) { }

            var pendingId = clientState.PendingFileTransferIds[0];
            var pendingFiletransfer = client.GetFileTransferById(pendingId).Value;

            var useMock =
                testServer.UseMockFileTransferStalledController(
                    pendingFiletransfer.TransferResponseCode,
                    5);

            if (useMock.Failure)
            {
                Assert.Fail(useMock.Error);
            }

            var transferResult = await client.AcceptInboundFileTransferAsync(pendingFiletransfer);
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
                stalledId = clientState.StalledFileTransferIds[0];
            }

            // Notify server that 1st transfer attempt is stalled
            var notifyTransferStalled =
                await client.SendNotificationFileTransferStalledAsync(stalledId).ConfigureAwait(false);

            if (notifyTransferStalled.Failure)
            {
                Assert.Fail("There was an error notifying the remote server that the file transfer has stalled: " + transferResult.Error);
            }

            // Wait for 1st transfer attempt cancelled notification
            while (!_serverWasNotifiedFileTransferStalled) { }

            var useOriginal =
                testServer.UseOriginalFileTransferController(
                    pendingFiletransfer.TransferResponseCode,
                    5);

            if (useOriginal.Failure)
            {
                Assert.Fail(useOriginal.Error);
            }

            _clientFileTransferPending = false;

            // 2nd transfer attempt
            var retryFileTransfer = await
                client.RetryFileTransferAsync(stalledId).ConfigureAwait(false);

            if (retryFileTransfer.Failure)
            {
                Assert.Fail("There was an error attempting to retry the stalled file transfer: " + transferResult.Error);
            }

            // Wait for 2nd transfer attempt
            while (!_clientFileTransferPending) { }

            var pendingId2 = clientState.PendingFileTransferIds[0];
            var pendingFiletransfer2 = client.GetFileTransferById(pendingId2).Value;

            var transferResult2 = await client.AcceptInboundFileTransferAsync(pendingFiletransfer2);
            if (transferResult2.Failure)
            {
                Assert.Fail("There was an error receiving the file from the remote server: " + transferResult2.Error);
            }

            // Wait for all file bytes to be received
            while (!_clientReceiveFileBytesComplete)
            {
                if (_clientErrorOccurred)
                {
                    Assert.Fail("File transfer failed");
                }
            }

            // Wait for server to receive confirmation message
            while (!_clientConfirmedFileTransferComplete) { }

            Assert.IsTrue(File.Exists(_localFilePath));
            Assert.AreEqual(FileName, Path.GetFileName(_localFilePath));

            var receivedFileSize = new FileInfo((_localFilePath)).Length;
            Assert.AreEqual(sentFileSize, receivedFileSize);

            await ShutdownServerAsync(client, runClientTask);
            await ShutdownServerAsync(testServer, runServerTask);

            if (_generateLogFiles)
            {
                File.AppendAllLines(clientLogFilePath, _clientLogMessages);
                File.AppendAllLines(serverLogFilePath, _serverLogMessages);
            }
        }
        
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
            while (!_clientFileTransferPending) { }

            var pendingId = _clientState.PendingFileTransferIds[0];
            var pendingFiletransfer = _client.GetFileTransferById(pendingId).Value;

            var useMock =
                _testServer.UseMockFileTransferStalledController(
                    pendingFiletransfer.TransferResponseCode,
                    5);

            if (useMock.Failure)
            {
                Assert.Fail(useMock.Error);
            }

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
            while (!_serverWasNotifiedFileTransferStalled) { }

            var useOriginal =
                _testServer.UseOriginalFileTransferController(
                    pendingFiletransfer.TransferResponseCode,
                    5);

            if (useOriginal.Failure)
            {
                Assert.Fail(useOriginal.Error);
            }

            _clientFileTransferPending = false;
            _serverStoppedSendingFileBytes = false;
            _serverWasNotifiedFileTransferStalled = false;

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
            while (!_clientFileTransferPending) { }

            var pendingId2 = _clientState.PendingFileTransferIds[0];
            var pendingFiletransfer2 = _client.GetFileTransferById(pendingId2).Value;

            var useMock2 =
                _testServer.UseMockFileTransferStalledController(
                    pendingFiletransfer2.TransferResponseCode,
                    5);

            if (useMock2.Failure)
            {
                Assert.Fail(useMock2.Error);
            }

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
            while (!_serverWasNotifiedFileTransferStalled) { }

            var useOriginal2 =
                _testServer.UseOriginalFileTransferController(
                    pendingFiletransfer.TransferResponseCode,
                    5);

            if (useOriginal2.Failure)
            {
                Assert.Fail(useOriginal2.Error);
            }

            _clientFileTransferPending = false;
            _serverStoppedSendingFileBytes = false;
            _serverWasNotifiedFileTransferStalled = false;
            _clientWasNotifiedRetryLimitExceeded = false;

            /////////////////////////////////////////////////////////////////////////////////////////////
            //
            // 3rd transfer attempt
            var retryFileTransfer2 = await
                _client.RetryFileTransferAsync(stalledId2).ConfigureAwait(false);

            if (retryFileTransfer2.Failure)
            {
                Assert.Fail("There was an error attempting to retry the stalled file transfer: " + retryFileTransfer2.Error);
            }

            while (!_clientWasNotifiedRetryLimitExceeded) { }

            var stalledFileTransfer2 = _client.GetFileTransferById(stalledId2).Value;
            Assert.IsFalse(stalledFileTransfer2.RetryLockoutExpired);

            _clientWasNotifiedRetryLimitExceeded = false;

            /////////////////////////////////////////////////////////////////////////////////////////////
            //
            // 4th transfer attempt
            var retryFileTransfer3 = await
                _client.RetryFileTransferAsync(stalledId2).ConfigureAwait(false);

            if (retryFileTransfer3.Failure)
            {
                Assert.Fail("There was an error attempting to retry the stalled file transfer: " + retryFileTransfer3.Error);
            }

            await Task.Delay(_serverSettings.RetryLimitLockout + TimeSpan.FromSeconds(1), token);
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
            while (!_clientFileTransferPending) { }

            var pendingId3 = _clientState.PendingFileTransferIds[0];
            var pendingFiletransfer3 = _client.GetFileTransferById(pendingId3).Value;

            var transferResult3 = await _client.AcceptInboundFileTransferAsync(pendingFiletransfer3);
            if (transferResult3.Failure)
            {
                Assert.Fail("There was an error receiving the file from the remote server: " + transferResult3.Error);
            }

            // Wait for all file bytes to be received
            while (!_clientReceiveFileBytesComplete)
            {
                if (_clientErrorOccurred)
                {
                    Assert.Fail("File transfer failed");
                }
            }

            // Wait for server to receive confirmation message
            while (!_clientConfirmedFileTransferComplete) { }

            Assert.IsTrue(File.Exists(receivedFilePath));
            Assert.AreEqual(FileName, Path.GetFileName(receivedFilePath));

            var receivedFileSize = new FileInfo(receivedFilePath).Length;
            Assert.AreEqual(sentFileSize, receivedFileSize);

            await ShutdownServerAsync(_client, _runClientTask);
            await ShutdownServerAsync(_testServer, _runServerTask);

            if (_generateLogFiles)
            {
                File.AppendAllLines(_clientLogFilePath, _clientLogMessages);
                File.AppendAllLines(_serverLogFilePath, _serverLogMessages);
            }
        }

        [TestMethod]
        public async Task VerifyGetFileAndFileDoesNotExist()
        {

            _clientLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyGetFile_client.log";
            _serverLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyGetFile_server.log";

            _clientSettings.LocalServerPortNumber = 8025;
            _serverSettings.LocalServerPortNumber = 8026;

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

            FileHelper.DeleteFileIfAlreadyExists(getFilePath, 3);
            Assert.IsFalse(File.Exists(getFilePath));

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

            while (!_clientWasNotifiedFileDoesNotExist) { }

            await ShutdownServerAsync(_client, _runClientTask);
            await ShutdownServerAsync(_server, _runServerTask);

            if (_generateLogFiles)
            {
                File.AppendAllLines(_clientLogFilePath, _clientLogMessages);
                File.AppendAllLines(_serverLogFilePath, _serverLogMessages);
            }
        }

        [TestMethod]
        public async Task VerifyGetFileAndFileAlreadyExists()
        {
            _clientLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyGetFileAndFileAlreadyExists_client.log";
            _serverLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyGetFileAndFileAlreadyExists_server.log";

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

            await ShutdownServerAsync(_client, _runClientTask);
            await ShutdownServerAsync(_server, _runServerTask);

            if (_generateLogFiles)
            {
                File.AppendAllLines(_clientLogFilePath, _clientLogMessages);
                File.AppendAllLines(_serverLogFilePath, _serverLogMessages);
            }
        }
    }
}
