
namespace AaronLuna.AsyncFileServerTest
{
    using System.Collections.Generic;
    using System.IO;
    using System.Threading.Tasks;

    using Microsoft.VisualStudio.TestTools.UnitTesting;

    using Common.Logging;

    public partial class AsyncFileServerTestFixture
    {
        [TestMethod]
        public async Task VerifyRequestFileList()
        {
            _clientLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyRequestFileList_client.log";
            _serverLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyRequestFileList_server.log";

            _clientSettings.LocalServerPortNumber = 8013;
            _serverSettings.LocalServerPortNumber = 8014;

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

            var fileListRequest =
                await _client.RequestFileListAsync(
                        _localIp,
                        _serverSettings.LocalServerPortNumber,
                        _testFilesFolder)
                    .ConfigureAwait(false);

            if (fileListRequest.Failure)
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

            await ShutdownServerAsync(_client, _runClientTask);
            await ShutdownServerAsync(_server, _runServerTask);

            if (_generateLogFiles)
            {
                File.AppendAllLines(_clientLogFilePath, _clientLogMessages);
                File.AppendAllLines(_serverLogFilePath, _serverLogMessages);
            }
        }

        [TestMethod]
        public async Task VerifyRequestFileListAndFolderIsEmpty()
        {
            _clientLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyRequestFileListAndFolderIsEmpty_client.log";
            _serverLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyRequestFileListAndFolderIsEmpty_server.log";

            _clientSettings.LocalServerPortNumber = 8019;
            _serverSettings.LocalServerPortNumber = 8020;

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

            var fileListRequest1 =
                await _client.RequestFileListAsync(
                    _localIp,
                    _serverSettings.LocalServerPortNumber,
                    _emptyFolder).ConfigureAwait(false);

            if (fileListRequest1.Failure)
            {
                Assert.Fail("Error sending request for transfer folder path.");
            }

            while (!_clientWasNotifiedFolderIsEmpty) { }
            
            await ShutdownServerAsync(_client, _runClientTask);
            await ShutdownServerAsync(_server, _runServerTask);

            if (_generateLogFiles)
            {
                File.AppendAllLines(_clientLogFilePath, _clientLogMessages);
                File.AppendAllLines(_serverLogFilePath, _serverLogMessages);
            }
        }

        [TestMethod]
        public async Task VerifyRequestFileListAndFolderDoesNotExist()
        {
            _clientLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyRequestFileListAndFolderDoesNotExist_client.log";
            _serverLogFilePath = $"{Logging.GetTimeStampForFileName()}_VerifyRequestFileListAndFolderDoesNotExist_server.log";

            _clientSettings.LocalServerPortNumber = 8021;
            _serverSettings.LocalServerPortNumber = 8022;

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

            Assert.IsFalse(Directory.Exists(_tempFolder));

            var fileListRequest1 =
                await _client.RequestFileListAsync(
                        _localIp,
                        _serverSettings.LocalServerPortNumber,
                        _tempFolder)
                    .ConfigureAwait(false);

            if (fileListRequest1.Failure)
            {
                Assert.Fail("Error sending request for transfer folder path.");
            }

            while (!_clientWasNotifiedFolderDoesNotExist) { }

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
