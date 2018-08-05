namespace AaronLuna.AsyncFileServerTest
{
    using System.Collections.Generic;
    using System.IO;
    using System.Threading.Tasks;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    using Common.Logging;

    using AsyncFileServer.Controller;

    public partial class AsyncFileServerTestFixture
    {
        [TestMethod]
        public async Task VerifyRequestFileList()
        {
            var clientLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyRequestFileList_client.log";
            var serverLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyRequestFileList_server.log";

            _clientSettings.LocalServerPortNumber = 8013;
            _serverSettings.LocalServerPortNumber = 8014;

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

            var getFileList =
                await client.RequestFileListAsync(
                        server.MyInfo.LocalIpAddress,
                        server.MyInfo.PortNumber,
                        _testFilesFolder)
                    .ConfigureAwait(false);

            if (getFileList.Failure)
            {
                Assert.Fail("Error sending request for transfer folder path.");
            }

            while (!_clientReceivedFileInfoList) { }

            Assert.AreEqual(4, _fileInfoList.Count);

            var fiDictionaryActual = new Dictionary<string, long>();
            foreach (var (fileName, folderPath, fileSizeBytes) in _fileInfoList)
            {
                var filePath = Path.Combine(folderPath, fileName);
                fiDictionaryActual.Add(filePath, fileSizeBytes);
            }

            var expectedFileNames = new List<string>
            {
                Path.Combine(_testFilesFolder, "fake.exe"),
                Path.Combine(_testFilesFolder, "loremipsum1.txt"),
                Path.Combine(_testFilesFolder, "loremipsum2.txt"),
                Path.Combine(_testFilesFolder, "smallFile.jpg")
            };

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

            await ShutdownServerAsync(client, runClientTask);
            await ShutdownServerAsync(server, runServerTask);

            if (_generateLogFiles)
            {
                File.AppendAllLines(clientLogFilePath, _clientLogMessages);
                File.AppendAllLines(serverLogFilePath, _serverLogMessages);
            }
        }

        [TestMethod]
        public async Task VerifyRequestFileListAndFolderIsEmpty()
        {
            var clientLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyRequestFileListAndFolderIsEmpty_client.log";
            var serverLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyRequestFileListAndFolderIsEmpty_server.log";

            _clientSettings.LocalServerPortNumber = 8019;
            _serverSettings.LocalServerPortNumber = 8020;

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

            var getFileList =
                await client.RequestFileListAsync(
                    server.MyInfo.LocalIpAddress,
                    server.MyInfo.PortNumber,
                    _emptyFolder).ConfigureAwait(false);

            if (getFileList.Failure)
            {
                Assert.Fail("Error sending request for transfer folder path.");
            }

            while (!_clientWasNotifiedFolderIsEmpty) { }
            
            await ShutdownServerAsync(client, runClientTask);
            await ShutdownServerAsync(server, runServerTask);

            if (_generateLogFiles)
            {
                File.AppendAllLines(clientLogFilePath, _clientLogMessages);
                File.AppendAllLines(serverLogFilePath, _serverLogMessages);
            }
        }

        [TestMethod]
        public async Task VerifyRequestFileListAndFolderDoesNotExist()
        {
            var clientLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyRequestFileListAndFolderDoesNotExist_client.log";
            var serverLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyRequestFileListAndFolderDoesNotExist_server.log";

            _clientSettings.LocalServerPortNumber = 8021;
            _serverSettings.LocalServerPortNumber = 8022;

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

            Assert.IsFalse(Directory.Exists(_tempFolder));

            var fileListRequest1 =
                await client.RequestFileListAsync(
                        server.MyInfo.LocalIpAddress,
                        server.MyInfo.PortNumber,
                        _tempFolder)
                    .ConfigureAwait(false);

            if (fileListRequest1.Failure)
            {
                Assert.Fail("Error sending request for transfer folder path.");
            }

            while (!_clientWasNotifiedFolderDoesNotExist) { }

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
