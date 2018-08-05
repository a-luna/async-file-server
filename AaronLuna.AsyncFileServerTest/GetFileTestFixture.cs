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
            var clientLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyGetFile_client.log";
            var serverLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyGetFile_server.log";

            _clientSettings.LocalServerPortNumber = 8005;
            _serverSettings.LocalServerPortNumber = 8006;
            
            var token = _cts.Token;

            var server = new AsyncFileServer("server", _serverSettings);
            server.EventOccurred += HandleServerEvent;
            server.SocketEventOccurred += HandleServerEvent;

            var client = new AsyncFileServer("client", _clientSettings);
            client.EventOccurred += HandleClientEvent;
            client.SocketEventOccurred += HandleClientEvent;

            var clientState = new ServerState(client);

            await server.InitializeAsync(_serverSettings).ConfigureAwait(false);
            await client.InitializeAsync(_clientSettings).ConfigureAwait(false);

            var runServerTask = Task.Run(() => server.RunAsync(token), token);
            var runClientTask = Task.Run(() => client.RunAsync(token), token);

            while (!server.IsListening) { }
            while (!client.IsListening) { }

            Assert.IsTrue(File.Exists(_remoteFilePath));
            var fileSizeInBytes = new FileInfo(_remoteFilePath).Length;

            FileHelper.DeleteFileIfAlreadyExists(_localFilePath, 3);
            Assert.IsFalse(File.Exists(_localFilePath));

            var getFile =
                await client.GetFileAsync(
                    server.MyInfo.LocalIpAddress,
                    server.MyInfo.PortNumber,
                    server.MyInfo.Name,
                    FileName,
                    fileSizeInBytes,
                    _remoteFolder,
                    _localFolder).ConfigureAwait(false);

            if (getFile.Failure)
            {
                var getFileError = "There was an error requesting the file from the remote server: " + getFile.Error;
                Assert.Fail(getFileError);
            }

            while (!_clientFileTransferPending) { }

            var pendingFileTransferId = clientState.PendingFileTransferIds[0];
            var pendingFileTransfer = client.GetFileTransferById(pendingFileTransferId).Value;

            var transferResult = await client.AcceptInboundFileTransferAsync(pendingFileTransfer);
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

            Assert.IsTrue(File.Exists(_localFilePath));
            Assert.AreEqual(fileSizeInBytes, new FileInfo(_localFilePath).Length);

            await ShutdownServerAsync(client, runClientTask);
            await ShutdownServerAsync(server, runServerTask);

            if (_generateLogFiles)
            {
                File.AppendAllLines(clientLogFilePath, _clientLogMessages);
                File.AppendAllLines(serverLogFilePath, _serverLogMessages);
            }
        }

        [TestMethod]
        public async Task VerifyGetFileAfterStalledFileTransfer()
        {
            var clientLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyGetFileAfterStalledFileTransfer_client.log";
            var serverLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyGetFileAfterStalledFileTransfer_testServer.log";

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
            var fileSizeInBytes = new FileInfo(_remoteFilePath).Length;

            FileHelper.DeleteFileIfAlreadyExists(_localFilePath, 3);
            Assert.IsFalse(File.Exists(_localFilePath));

            // 1st transfer attempt
            var getFile =
                await client.GetFileAsync(
                    testServer.MyInfo.LocalIpAddress,
                    testServer.MyInfo.PortNumber,
                    testServer.MyInfo.Name,
                    FileName,
                    fileSizeInBytes,
                    _remoteFolder,
                    _localFolder).ConfigureAwait(false);

            if (getFile.Failure)
            {
                var getFileError = "There was an error requesting the file from the remote server: " + getFile.Error;
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
            Assert.AreEqual(fileSizeInBytes, new FileInfo((_localFilePath)).Length);

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
            var clientLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyGetFileAfterRetryLimitExceeded_client.log";
            var serverLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyGetFileAfterRetryLimitExceeded_testServer.log";

            _clientSettings.LocalServerPortNumber = 8009;
            _serverSettings.LocalServerPortNumber = 8010;

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
            var fileSizeInBytes = new FileInfo(_remoteFilePath).Length;

            FileHelper.DeleteFileIfAlreadyExists(_localFilePath, 3);
            Assert.IsFalse(File.Exists(_localFilePath));

            // 1st transfer attempt
            var getFile =
                await client.GetFileAsync(
                    testServer.MyInfo.LocalIpAddress,
                    testServer.MyInfo.PortNumber,
                    testServer.MyInfo.Name,
                    FileName,
                    fileSizeInBytes,
                    _remoteFolder,
                    _localFolder).ConfigureAwait(false);

            if (getFile.Failure)
            {
                var getFileError = "There was an error requesting the file from the remote server: " + getFile.Error;
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
            _serverStoppedSendingFileBytes = false;
            _serverWasNotifiedFileTransferStalled = false;

            /////////////////////////////////////////////////////////////////////////////////////////////
            //
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

            var useMock2 =
                testServer.UseMockFileTransferStalledController(
                    pendingFiletransfer2.TransferResponseCode,
                    5);

            if (useMock2.Failure)
            {
                Assert.Fail(useMock2.Error);
            }

            var transferResult2 = await client.AcceptInboundFileTransferAsync(pendingFiletransfer2);
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
                stalledId2 = clientState.StalledFileTransferIds[0];
            }

            // Notify server that 2nd transfer attempt is stalled
            var notifyTransferStalled2 =
                await client.SendNotificationFileTransferStalledAsync(stalledId2).ConfigureAwait(false);

            if (notifyTransferStalled2.Failure)
            {
                Assert.Fail("There was an error notifying the remote server that the file transfer has stalled: " + notifyTransferStalled2.Error);
            }

            // Wait for 2nd transfer attempt cancelled notification
            while (!_serverWasNotifiedFileTransferStalled) { }

            var useOriginal2 =
                testServer.UseOriginalFileTransferController(
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
                client.RetryFileTransferAsync(stalledId2).ConfigureAwait(false);

            if (retryFileTransfer2.Failure)
            {
                Assert.Fail("There was an error attempting to retry the stalled file transfer: " + retryFileTransfer2.Error);
            }

            while (!_clientWasNotifiedRetryLimitExceeded) { }

            var stalledFileTransfer2 = client.GetFileTransferById(stalledId2).Value;
            Assert.IsFalse(stalledFileTransfer2.RetryLockoutExpired);

            _clientWasNotifiedRetryLimitExceeded = false;

            /////////////////////////////////////////////////////////////////////////////////////////////
            //
            // 4th transfer attempt
            var retryFileTransfer3 = await
                client.RetryFileTransferAsync(stalledId2).ConfigureAwait(false);

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
                client.RetryFileTransferAsync(stalledId2).ConfigureAwait(false);

            if (retryFileTransfer4.Failure)
            {
                Assert.Fail("There was an error attempting to retry the stalled file transfer: " + retryFileTransfer4.Error);
            }

            // Wait for 5th transfer attempt
            while (!_clientFileTransferPending) { }

            var pendingId3 = clientState.PendingFileTransferIds[0];
            var pendingFiletransfer3 = client.GetFileTransferById(pendingId3).Value;

            var transferResult3 = await client.AcceptInboundFileTransferAsync(pendingFiletransfer3);
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

            Assert.IsTrue(File.Exists(_localFilePath));
            Assert.AreEqual(fileSizeInBytes, new FileInfo(_localFilePath).Length);

            await ShutdownServerAsync(client, runClientTask);
            await ShutdownServerAsync(testServer, runServerTask);

            if (_generateLogFiles)
            {
                File.AppendAllLines(clientLogFilePath, _clientLogMessages);
                File.AppendAllLines(serverLogFilePath, _serverLogMessages);
            }
        }

        [TestMethod]
        public async Task VerifyGetFileAndFileDoesNotExist()
        {

            var clientLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyGetFileAndFileDoesNotExist_client.log";
            var serverLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyGetFileAndFileDoesNotExist_server.log";

            _clientSettings.LocalServerPortNumber = 8025;
            _serverSettings.LocalServerPortNumber = 8026;

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

            Assert.IsTrue(File.Exists(_remoteFilePath));
            var fileSizeInBytes = new FileInfo(_remoteFilePath).Length;

            FileHelper.DeleteFileIfAlreadyExists(_remoteFilePath, 3);
            Assert.IsFalse(File.Exists(_remoteFilePath));

            FileHelper.DeleteFileIfAlreadyExists(_localFilePath, 3);
            Assert.IsFalse(File.Exists(_localFilePath));

            var getFile =
                await client.GetFileAsync(
                    server.MyInfo.LocalIpAddress,
                    server.MyInfo.PortNumber,
                    server.MyInfo.Name,
                    FileName,
                    fileSizeInBytes,
                    _remoteFolder,
                    _localFolder).ConfigureAwait(false);

            if (getFile.Failure)
            {
                var getFileError = "There was an error requesting the file from the remote server: " + getFile.Error;
                Assert.Fail(getFileError);
            }

            while (!_clientWasNotifiedFileDoesNotExist) { }

            await ShutdownServerAsync(client, runClientTask);
            await ShutdownServerAsync(server, runServerTask);

            if (_generateLogFiles)
            {
                File.AppendAllLines(clientLogFilePath, _clientLogMessages);
                File.AppendAllLines(serverLogFilePath, _serverLogMessages);
            }
        }

        [TestMethod]
        public async Task VerifyGetFileAndFileAlreadyExists()
        {
            var clientLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyGetFileAndFileAlreadyExists_client.log";
            var serverLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyGetFileAndFileAlreadyExists_server.log";

            _clientSettings.LocalServerPortNumber = 8017;
            _serverSettings.LocalServerPortNumber = 8018;

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

            Assert.IsTrue(File.Exists(_localFilePath));
            Assert.IsTrue(File.Exists(_remoteFilePath));
            var fileSizeInBytes = new FileInfo(_remoteFilePath).Length;
            
            var getFile =
                await client.GetFileAsync(
                    server.MyInfo.LocalIpAddress,
                    server.MyInfo.PortNumber,
                    server.MyInfo.Name,
                    FileName,
                    fileSizeInBytes,
                    _remoteFolder,
                    _localFolder).ConfigureAwait(false);

            if (getFile.Failure)
            {
                var getFileError = "There was an error requesting the file from the remote server: " + getFile.Error;
                Assert.Fail(getFileError);
            }

            while (!_clientRejectedFileTransfer) { }

            await ShutdownServerAsync(client, runClientTask);
            await ShutdownServerAsync(server, runServerTask);

            if (_generateLogFiles)
            {
                File.AppendAllLines(clientLogFilePath, _clientLogMessages);
                File.AppendAllLines(serverLogFilePath, _serverLogMessages);
            }
        }
    }
}
