using System;
using System.IO;
using System.Net.Mime;
using System.Threading.Tasks;
using AaronLuna.AsyncSocketServer;
using AaronLuna.Common.Extensions;
using AaronLuna.Common.IO;
using AaronLuna.Common.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SixLabors.ImageSharp;

namespace AaronLuna.AsyncSocketServerTest
{
    public partial class AsyncServerTestFixture
    {
        [TestMethod]
        public async Task VerifySendFile()
        {
            var clientLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifySendFile_client.log";
            var serverLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifySendFile_server.log";

            _serverSettings.LocalServerPortNumber = 8003;
            _clientSettings.LocalServerPortNumber = 8004;

            var token = _cts.Token;

            var server = new AsyncServer(_serverSettings);
            server.EventOccurred += HandleServerEvent;
            server.SocketEventOccurred += HandleServerEvent;

            var client = new AsyncServer(_clientSettings);
            client.EventOccurred += HandleClientEvent;
            client.SocketEventOccurred += HandleClientEvent;

            await server.InitializeAsync("server").ConfigureAwait(false);
            await client.InitializeAsync("client").ConfigureAwait(false);

            var serverState = new ServerState(server);

            var runServerTask = Task.Run(() => server.RunAsync(token), token);
            var runClientTask = Task.Run(() => client.RunAsync(token), token);

            while (!server.IsRunning) { }
            while (!client.IsRunning) { }

            var fileSizeInBytes = new FileInfo(_localFilePath).Length;
            FileHelper.DeleteFileIfAlreadyExists(_remoteFilePath, 3);
            Assert.IsFalse(File.Exists(_remoteFilePath));

            var sendFileResult =
                await client.SendFileAsync(
                    server.MyInfo,
                    FileName,
                    fileSizeInBytes,
                    _localFolder,
                    _remoteFolder).ConfigureAwait(false);

            if (sendFileResult.Failure)
            {
                Assert.Fail("There was an error sending the file to the remote server: " + sendFileResult.Error);
            }

            while (!_serverFileTransferPending) { }

            var pendingFileTransferId = serverState.PendingFileTransferIds[0];
            var pendingFileTransfer = server.GetFileTransferById(pendingFileTransferId).Value;

            var transferResult = await server.AcceptInboundFileTransferAsync(pendingFileTransfer);
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

            Assert.IsTrue(File.Exists(_remoteFilePath));
            Assert.AreEqual(fileSizeInBytes, new FileInfo(_remoteFilePath).Length);

            var receiveImageHeight = 0;
            var receiveImageWidth = 0;

            try
            {
                using (var receiveImage = Image.Load(_remoteFilePath))
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

            using (var sentImage = Image.Load(_localFilePath))
            {
                Assert.AreEqual(sentImage.Height, receiveImageHeight);
                Assert.AreEqual(sentImage.Width, receiveImageWidth);
            }

            await ShutdownServerAsync(client, runClientTask);
            await ShutdownServerAsync(server, runServerTask);

            if (_generateLogFiles)
            {
                File.AppendAllLines(clientLogFilePath, _clientLogMessages);
                File.AppendAllLines(serverLogFilePath, _serverLogMessages);
            }
        }

        [TestMethod]
        public async Task VerifySendFileAndFileAlreadyExists()
        {
            var clientLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifySendFileAndFileAlreadyExists_client.log";
            var serverLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifySendFileAndFileAlreadyExists_server.log";

            _serverSettings.LocalServerPortNumber = 8015;
            _clientSettings.LocalServerPortNumber = 8016;

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

            var fileSizeInBytes = new FileInfo(_localFilePath).Length;
            Assert.IsTrue(File.Exists(_remoteFilePath));

            var sendFileResult =
                await client.SendFileAsync(
                        server.MyInfo,
                        FileName,
                        fileSizeInBytes,
                        _localFolder,
                        _remoteFolder)
                    .ConfigureAwait(false);

            if (sendFileResult.Failure)
            {
                Assert.Fail("Error occurred sending outbound file request to server");
            }

            while (!_serverRejectedFileTransfer) { }

            await ShutdownServerAsync(client, runClientTask);
            await ShutdownServerAsync(server, runServerTask);

            if (_generateLogFiles)
            {
                File.AppendAllLines(clientLogFilePath, _clientLogMessages);
                File.AppendAllLines(serverLogFilePath, _serverLogMessages);
            }
        }

        [TestMethod]
        public async Task VerifySendFileAndRejectTransfer()
        {
            var clientLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifySendFileAndRejectTransfer_client.log";
            var serverLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifySendFileAndRejectTransfer_server.log";

            _serverSettings.LocalServerPortNumber = 8023;
            _clientSettings.LocalServerPortNumber = 8024;

            var token = _cts.Token;

            var server = new AsyncServer(_serverSettings);
            server.EventOccurred += HandleServerEvent;
            server.SocketEventOccurred += HandleServerEvent;

            var client = new AsyncServer(_clientSettings);
            client.EventOccurred += HandleClientEvent;
            client.SocketEventOccurred += HandleClientEvent;

            await server.InitializeAsync("server").ConfigureAwait(false);
            await client.InitializeAsync("client").ConfigureAwait(false);

            var serverState = new ServerState(server);

            var runServerTask = Task.Run(() => server.RunAsync(token), token);
            var runClientTask = Task.Run(() => client.RunAsync(token), token);

            while (!server.IsRunning) { }
            while (!client.IsRunning) { }

            var fileSizeInBytes = new FileInfo(_localFilePath).Length;
            FileHelper.DeleteFileIfAlreadyExists(_remoteFilePath, 3);
            Assert.IsFalse(File.Exists(_remoteFilePath));

            var sendFileResult =
                await client.SendFileAsync(
                    server.MyInfo,
                    FileName,
                    fileSizeInBytes,
                    _localFolder,
                    _remoteFolder).ConfigureAwait(false);

            if (sendFileResult.Failure)
            {
                Assert.Fail("There was an error sending the file to the remote server: " + sendFileResult.Error);
            }

            while (!_serverFileTransferPending) { }

            var pendingFileTransferId = serverState.PendingFileTransferIds[0];
            var pendingFileTransfer = server.GetFileTransferById(pendingFileTransferId).Value;

            var transferResult = await server.RejectInboundFileTransferAsync(pendingFileTransfer);
            if (transferResult.Failure)
            {
                Assert.Fail("There was an error receiving the file from the remote server: " + transferResult.Error);
            }

            while (!_serverRejectedFileTransfer) { }

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
