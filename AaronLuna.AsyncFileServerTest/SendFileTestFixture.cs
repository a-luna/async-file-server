namespace AaronLuna.AsyncFileServerTest
{
    using System;
    using System.IO;
    using System.Threading.Tasks;

    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using SixLabors.ImageSharp;

    using Common.Extensions;
    using Common.IO;
    using Common.Logging;

    public partial class AsyncFileServerTestFixture
    {
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

            while (!_serverFileTransferPending) { }

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

            while (!_serverConfirmedFileTransferComplete) { }

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

            await ShutdownServerAsync(_client, _runClientTask);
            await ShutdownServerAsync(_server, _runServerTask);

            if (_generateLogFiles)
            {
                File.AppendAllLines(_clientLogFilePath, _clientLogMessages);
                File.AppendAllLines(_serverLogFilePath, _serverLogMessages);
            }
        }

        [TestMethod]
        public async Task VerifySendFileAndFileAlreadyExists()
        {
            _clientLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifySendFileAndFileAlreadyExists_client.log";
            _serverLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifySendFileAndFileAlreadyExists_server.log";

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

            await ShutdownServerAsync(_client, _runClientTask);
            await ShutdownServerAsync(_server, _runServerTask);

            if (_generateLogFiles)
            {
                File.AppendAllLines(_clientLogFilePath, _clientLogMessages);
                File.AppendAllLines(_serverLogFilePath, _serverLogMessages);
            }
        }

        [TestMethod]
        public async Task VerifySendFileAndRejectTransfer()
        {
            _clientLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifySendFileAndRejectTransfer_client.log";
            _serverLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifySendFileAndRejectTransfer_server.log";

            _serverSettings.LocalServerPortNumber = 8023;
            _clientSettings.LocalServerPortNumber = 8024;

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

            while (!_serverFileTransferPending) { }

            var pendingFileTransferId = _serverState.PendingFileTransferIds[0];
            var pendingFileTransfer = _server.GetFileTransferById(pendingFileTransferId).Value;

            var transferResult = await _server.RejectInboundFileTransferAsync(pendingFileTransfer);
            if (transferResult.Failure)
            {
                Assert.Fail("There was an error receiving the file from the remote server: " + transferResult.Error);
            }

            while (!_serverRejectedFileTransfer) { }

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
