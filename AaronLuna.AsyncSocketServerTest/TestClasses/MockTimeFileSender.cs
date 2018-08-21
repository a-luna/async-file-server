using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using AaronLuna.AsyncSocketServer;
using AaronLuna.AsyncSocketServer.FileTransfers;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncSocketServerTest.TestClasses
{
    public class MockTimeFileSender : FileSender
    {
        readonly TimeSpan _duration;

        public MockTimeFileSender(ServerSettings settings, TimeSpan duration) : base(settings)
        {
            _duration = duration;
        }

        public override async Task<Result> SendFileAsync(
            FileTransfer fileTransfer,
            Socket socket,
            CancellationToken token)
        {
            fileTransfer.Status = FileTransferStatus.InProgress;
            fileTransfer.TransferStartTime = DateTime.Now;

            OnEventOccurred(new ServerEvent
            {
                EventType = EventType.SendFileBytesStarted,
                RemoteServerIpAddress = fileTransfer.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = fileTransfer.RemoteServerInfo.PortNumber,
                FileTransferId = fileTransfer.Id
            });

            fileTransfer.BytesRemaining = fileTransfer.FileSizeInBytes;
            fileTransfer.FileChunkSentCount = 0;
            fileTransfer.OutboundFileTransferStalled = false;

            Thread.Sleep(_duration);

            fileTransfer.Status = FileTransferStatus.TransferComplete;
            fileTransfer.TransferCompleteTime = DateTime.Now;
            fileTransfer.PercentComplete = 1;
            fileTransfer.CurrentBytesSent = 0;
            fileTransfer.BytesRemaining = 0;

            OnEventOccurred(new ServerEvent
            {
                EventType = EventType.SendFileBytesComplete,
                RemoteServerIpAddress = fileTransfer.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = fileTransfer.RemoteServerInfo.PortNumber,
                FileTransferId = fileTransfer.Id
            });

            await Task.Delay(10);
            return Result.Ok();
        }
    }
}
