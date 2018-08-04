using System;
using System.Collections.Generic;
using System.Linq;
using AaronLuna.AsyncFileServer.Controller;
using AaronLuna.AsyncFileServer.Model;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncFileServerTest.TestClasses
{
    class AsyncFileServerTest : AsyncFileServer.Controller.AsyncFileServer
    {
        readonly ServerSettings _testSettings;
        FileTransferController _originalFileTransfer;

        public AsyncFileServerTest(string name, ServerSettings settings) : base(name, settings)
        {
            UpdateSettings(settings);
            _testSettings = settings;
        }

        public event EventHandler<ServerEvent> TestEventOccurred;
        public event EventHandler<ServerEvent> TestSocketEventOccurred;
        public event EventHandler<ServerEvent> TestFileTransferProgress;

        void HandleTestEventOccurred(object sender, ServerEvent e)
        {
            TestEventOccurred?.Invoke(sender, e);
        }

        void HandleTestSocketEventOccurred(object sender, ServerEvent e)
        {
            TestSocketEventOccurred?.Invoke(sender, e);
        }

        void HandleTestFileTransferProgress(object sender, ServerEvent e)
        {
            TestFileTransferProgress?.Invoke(sender, e);
        }

        public Result UseMockFileTransferSendController(long responseCode, int maxAttempts, TimeSpan duration)
        {
            var getControllser = GetController(responseCode, maxAttempts);
            if (getControllser.Failure)
            {
                return getControllser;
            }

            _originalFileTransfer = getControllser.Value;

            var mockFileTransfer =
                new MockFileTransferSendController(_originalFileTransfer.Id, _testSettings, duration);

            InitializeMockFileTransferController(mockFileTransfer);
            mockFileTransfer.TestEventOccurred += HandleTestEventOccurred;

            return Result.Ok();
        }

        public Result UseMockFileTransferReceiveController(long responseCode, int maxAttempts, TimeSpan duration)
        {
            var getControllser = GetController(responseCode, maxAttempts);
            if (getControllser.Failure)
            {
                return getControllser;
            }

            _originalFileTransfer = getControllser.Value;

            var mockFileTransfer =
                new MockFileTransferReceiveController(_originalFileTransfer.Id, _testSettings, duration);

            InitializeMockFileTransferController(mockFileTransfer);
            mockFileTransfer.TestEventOccurred += HandleTestEventOccurred;

            return Result.Ok();
        }

        public Result UseMockFileTransferStalledController(long responseCode, int maxAttempts)
        {
            var getControllser = GetController(responseCode, maxAttempts);
            if (getControllser.Failure)
            {
                return getControllser;
            }

            _originalFileTransfer = getControllser.Value;

            var mockFileTransfer = new MockFileTransferStalledController(_originalFileTransfer.Id, _testSettings)
            {
                RemoteServerTransferId = _originalFileTransfer.RemoteServerTransferId
            };

            InitializeMockFileTransferController(mockFileTransfer);
            mockFileTransfer.TestEventOccurred += HandleTestEventOccurred;
            mockFileTransfer.TestSocketEventOccurred += HandleTestSocketEventOccurred;
            mockFileTransfer.TestFileTransferProgress += HandleTestFileTransferProgress;

            return Result.Ok();
        }

        public Result UseOriginalFileTransferController(long responseCode, int maxAttempts)
        {
            var getControllser = GetController(responseCode, maxAttempts);
            if (getControllser.Failure)
            {
                return getControllser;
            }

            var mockFileTransfer = getControllser.Value;
            _originalFileTransfer.Status = mockFileTransfer.Status;
            _originalFileTransfer.RequestId = mockFileTransfer.RequestId;
            _originalFileTransfer.BytesRemaining = mockFileTransfer.BytesRemaining;
            _originalFileTransfer.FileChunkSentCount = mockFileTransfer.FileChunkSentCount;
            _originalFileTransfer.OutboundFileTransferStalled = mockFileTransfer.OutboundFileTransferStalled;
            _originalFileTransfer.CurrentBytesSent = mockFileTransfer.CurrentBytesSent;
            _originalFileTransfer.PercentComplete = mockFileTransfer.PercentComplete;

            FileTransfers.Remove(mockFileTransfer);
            FileTransfers.Add(_originalFileTransfer);

            return Result.Ok();
        }

        Result<FileTransferController> GetController(long responseCode, int maxAttempts)
        {
            var foundMatch = false;
            var attemptNumber = 1;
            var matches = new List<FileTransferController>();

            while (!foundMatch)
            {
                if (attemptNumber > maxAttempts)
                {
                    return Result.Fail<FileTransferController>(
                        $"There was an error retrieving the file transfer controller");

                }
                
                matches =
                    FileTransfers.Select(ft => ft)
                        .Where(ft => ft.TransferResponseCode == responseCode)
                        .ToList();

                if (matches.Count == 0)
                {
                    attemptNumber++;
                    continue;
                }

                foundMatch = true;
            }

            return Result.Ok(matches[0]);
        }

        void InitializeMockFileTransferController(FileTransferController mockFileTransfer)
        {
            mockFileTransfer.Initialize(
                _originalFileTransfer.TransferDirection,
                _originalFileTransfer.Initiator,
                _originalFileTransfer.LocalServerInfo,
                _originalFileTransfer.RemoteServerInfo,
                _originalFileTransfer.FileName,
                _originalFileTransfer.FileSizeInBytes,
                _originalFileTransfer.LocalFolderPath,
                _originalFileTransfer.RemoteFolderPath);

            mockFileTransfer.TransferResponseCode = _originalFileTransfer.TransferResponseCode;
            mockFileTransfer.RetryCounter = _originalFileTransfer.RetryCounter;

            FileTransfers.Remove(_originalFileTransfer);
            FileTransfers.Add(mockFileTransfer);
        }
    }
}
