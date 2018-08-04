namespace AaronLuna.AsyncFileServerTest.TestClasses
{
    using System;
    using System.Linq;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;

    using AsyncFileServer.Controller;
    using AsyncFileServer.Model;
    using Common.Result;

    class MockFileTransferSendController : FileTransferController
    {
        FileTransfer _fileTransfer;
        readonly TimeSpan _duration;

        public MockFileTransferSendController(
            int id,
            ServerSettings settings,
            TimeSpan duration) 
            : base(id, settings)
        {
            _fileTransfer = new FileTransfer();
            _duration = duration;
        }

        public event EventHandler<ServerEvent> TestEventOccurred;

        public override void Initialize(
            FileTransferDirection direction,
            FileTransferInitiator initiator,
            ServerInfo localServerInfo,
            ServerInfo remoteServerInfo,
            string fileName,
            long fileSizeInBytes,
            string localFolderPath,
            string remoteFolderPath)
        {
            TransferDirection = direction;
            Initiator = initiator;
            LocalServerInfo = localServerInfo;
            RemoteServerInfo = remoteServerInfo;
            Status = FileTransferStatus.Pending;
            RequestInitiatedTime = DateTime.Now;

            _fileTransfer = new FileTransfer
            {
                RemoteServerIpAddress = RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = RemoteServerInfo.PortNumber,
                FileName = fileName,
                LocalFolderPath = localFolderPath,
                RemoteFolderPath = remoteFolderPath,
                FileSizeInBytes = fileSizeInBytes
            };
        }

        public override async Task<Result> SendFileAsync(Socket socket, CancellationToken token)
        {
            Status = FileTransferStatus.InProgress;
            TransferStartTime = DateTime.Now;

            EventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.SendFileBytesStarted,
                RemoteServerIpAddress = _fileTransfer.RemoteServerIpAddress,
                RemoteServerPortNumber = _fileTransfer.RemoteServerPortNumber,
                FileTransferId = Id,
                RequestId = RequestId
            });

            TestEventOccurred?.Invoke(this, EventLog.Last());

            BytesRemaining = _fileTransfer.FileSizeInBytes;
            FileChunkSentCount = 0;
            OutboundFileTransferStalled = false;

            Thread.Sleep(_duration);

            Status = FileTransferStatus.TransferComplete;
            TransferCompleteTime = DateTime.Now;
            PercentComplete = 1;
            CurrentBytesSent = 0;
            BytesRemaining = 0;

            EventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.SendFileBytesComplete,
                RemoteServerIpAddress = _fileTransfer.RemoteServerIpAddress,
                RemoteServerPortNumber = _fileTransfer.RemoteServerPortNumber,
                RequestId = RequestId,
                FileTransferId = Id,
            });

            TestEventOccurred?.Invoke(this, EventLog.Last());

            await Task.Delay(10);
            return Result.Ok();
        }
    }
}
