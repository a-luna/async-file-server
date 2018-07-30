using System;
using System.Collections.Generic;
using System.Linq;
using AaronLuna.AsyncFileServer.Controller;
using AaronLuna.AsyncFileServer.Model;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncFileServer.Test.TestClasses
{
    class AsyncFileServerTest : Controller.AsyncFileServer
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

        public Result ChangeRegularControllerToStalledController(int maxAttempts)
        {
            var getControllser = GetController(maxAttempts);
            if (getControllser.Failure)
            {
                return getControllser;
            }

            _originalFileTransfer = getControllser.Value;            

            var stalledFileTransfer = new FileTransferStalledController(_originalFileTransfer.Id, _testSettings)
            {
                RemoteServerTransferId = _originalFileTransfer.RemoteServerTransferId
            };

            stalledFileTransfer.TestEventOccurred += HandleTestEventOccurred;
            stalledFileTransfer.TestSocketEventOccurred += HandleTestSocketEventOccurred;
            stalledFileTransfer.TestFileTransferProgress += HandleTestFileTransferProgress;
            
            stalledFileTransfer.Initialize(
                FileTransferDirection.Outbound,
                FileTransferInitiator.RemoteServer,
                _originalFileTransfer.LocalServerInfo,
                _originalFileTransfer.RemoteServerInfo,
                _originalFileTransfer.FileName,
                _originalFileTransfer.FileSizeInBytes,
                _originalFileTransfer.LocalFolderPath,
                _originalFileTransfer.RemoteFolderPath);

            stalledFileTransfer.TransferResponseCode = _originalFileTransfer.TransferResponseCode;
            stalledFileTransfer.RetryCounter = _originalFileTransfer.RetryCounter;

            FileTransfers.Remove(_originalFileTransfer);
            FileTransfers.Add(stalledFileTransfer);

            return Result.Ok();
        }

        public Result ChangeStalledControllerBackToRegularController(int maxAttempts)
        {
            var getControllser = GetController(maxAttempts);
            if (getControllser.Failure)
            {
                return getControllser;
            }

            var cancelledFileTransfer = getControllser.Value;
            _originalFileTransfer.Status = cancelledFileTransfer.Status;
            _originalFileTransfer.RequestId = cancelledFileTransfer.RequestId;
            _originalFileTransfer.BytesRemaining = cancelledFileTransfer.BytesRemaining;
            _originalFileTransfer.FileChunkSentCount = cancelledFileTransfer.FileChunkSentCount;
            _originalFileTransfer.OutboundFileTransferStalled = cancelledFileTransfer.OutboundFileTransferStalled;
            _originalFileTransfer.CurrentBytesSent = cancelledFileTransfer.CurrentBytesSent;
            _originalFileTransfer.PercentComplete = cancelledFileTransfer.PercentComplete;

            FileTransfers.Remove(cancelledFileTransfer);
            FileTransfers.Add(_originalFileTransfer);

            return Result.Ok();
        }

        Result<FileTransferController> GetController(int maxAttempts)
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

                var responseCodes = FileTransfers.Select(ft => ft.TransferResponseCode).ToList();
                if (responseCodes.Count == 0)
                {
                    attemptNumber++;
                    continue;
                }

                matches =
                    FileTransfers.Select(ft => ft)
                        .Where(ft => ft.TransferResponseCode == responseCodes[0])
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
    }
}
