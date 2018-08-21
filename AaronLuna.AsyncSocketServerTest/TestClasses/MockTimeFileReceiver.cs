using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using AaronLuna.AsyncSocketServer;
using AaronLuna.AsyncSocketServer.FileTransfers;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncSocketServerTest.TestClasses
{
    public class MockTimeFileReceiver : FileReceiver
    {
        readonly TimeSpan _duration;

        public MockTimeFileReceiver(ServerSettings settings, TimeSpan duration) :base(settings)
        {
            _duration = duration;
        }

        public override async Task<Result> ReceiveFileAsync(
            FileTransfer fileTransfer,
            Socket socket,
            byte[] unreadBytes,
            CancellationToken token)
        {
            fileTransfer.Status = FileTransferStatus.InProgress;
            fileTransfer.TransferStartTime = DateTime.Now;

            OnEventOccurred(new ServerEvent
            {
                EventType = EventType.ReceiveFileBytesStarted,
                FileTransferId = fileTransfer.Id,
                RemoteServerIpAddress = fileTransfer.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = fileTransfer.RemoteServerInfo.PortNumber,
                FileTransferStartTime = fileTransfer.TransferStartTime,
                FileName = fileTransfer.FileName,
                FileSizeInBytes = fileTransfer.FileSizeInBytes,
                LocalFolder = fileTransfer.LocalFolderPath,
                RetryCounter = fileTransfer.RetryCounter,
                RemoteServerRetryLimit = fileTransfer.RemoteServerRetryLimit
            });

            fileTransfer.TotalBytesReceived = 0;
            fileTransfer.BytesRemaining = fileTransfer.FileSizeInBytes;
            fileTransfer.PercentComplete = 0;
            fileTransfer.InboundFileTransferStalled = false;

            Thread.Sleep(_duration);

            fileTransfer.Status = FileTransferStatus.ConfirmedComplete;
            fileTransfer.TransferCompleteTime = DateTime.Now;
            fileTransfer.PercentComplete = 1;
            fileTransfer.CurrentBytesReceived = 0;
            fileTransfer.BytesRemaining = 0;

            OnEventOccurred(new ServerEvent
            {
                EventType = EventType.ReceiveFileBytesComplete,
                FileTransferStartTime = fileTransfer.TransferStartTime,
                FileTransferCompleteTime = DateTime.Now,
                FileSizeInBytes = fileTransfer.FileSizeInBytes,
                FileTransferRate = fileTransfer.TransferRate,
                RemoteServerIpAddress = fileTransfer.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = fileTransfer.RemoteServerInfo.PortNumber,
                FileTransferId = fileTransfer.Id
            });

            await Task.Delay(10);
            return Result.Ok();
        }
    }
}
