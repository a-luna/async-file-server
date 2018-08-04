namespace AaronLuna.AsyncFileServerTest.TestClasses
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;

    using AsyncFileServer.Controller;
    using AsyncFileServer.Model;
    using AsyncFileServer.Utilities;
    using Common.Result;

    class MockFileTransferStalledController : FileTransferController
    {
        readonly int _bufferSize;
        readonly int _timeoutMs;
        readonly float _updateInterval;
        byte[] _buffer;
        Socket _socket;
        FileTransfer _fileTransfer;

        public MockFileTransferStalledController(int id, ServerSettings settings) : base(id, settings)
        {
            _bufferSize = settings.SocketSettings.BufferSize;
            _timeoutMs = settings.SocketSettings.SocketTimeoutInMilliseconds;
            _updateInterval = settings.TransferUpdateInterval;
            _buffer = new byte[_bufferSize];
            _fileTransfer = new FileTransfer();
        }

        public event EventHandler<ServerEvent> TestEventOccurred;
        public event EventHandler<ServerEvent> TestSocketEventOccurred;
        public event EventHandler<ServerEvent> TestFileTransferProgress;

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
            _socket = socket;
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

            using (var file = File.OpenRead(_fileTransfer.LocalFilePath))
            {
                while (BytesRemaining > 0)
                {
                    if (token.IsCancellationRequested)
                    {
                        Status = FileTransferStatus.Cancelled;
                        TransferCompleteTime = DateTime.Now;
                        ErrorMessage = "Cancellation requested";

                        return Result.Ok();
                    }

                    var fileChunkSize = (int)Math.Min(_bufferSize, BytesRemaining);
                    _buffer = new byte[fileChunkSize];

                    var numberOfBytesToSend = file.Read(_buffer, 0, fileChunkSize);
                    BytesRemaining -= numberOfBytesToSend;

                    var offset = 0;
                    var socketSendCount = 0;
                    while (numberOfBytesToSend > 0)
                    {
                        var sendFileChunkResult =
                            await _socket.SendWithTimeoutAsync(
                                _buffer,
                                offset,
                                fileChunkSize,
                                SocketFlags.None,
                                _timeoutMs).ConfigureAwait(false);

                        if (OutboundFileTransferStalled)
                        {
                            const string fileTransferStalledErrorMessage =
                                "Aborting file transfer, client says that data is no longer being received (SendFileBytesAsync)";

                            Status = FileTransferStatus.Cancelled;
                            TransferCompleteTime = DateTime.Now;
                            ErrorMessage = fileTransferStalledErrorMessage;

                            return Result.Ok();
                        }

                        if (sendFileChunkResult.Failure)
                        {
                            return sendFileChunkResult;
                        }

                        CurrentBytesSent = sendFileChunkResult.Value;
                        numberOfBytesToSend -= CurrentBytesSent;
                        offset += CurrentBytesSent;
                        socketSendCount++;
                    }

                    CurrentBytesSent = fileChunkSize;
                    FileChunkSentCount++;

                    var checkPercentRemaining = BytesRemaining / (float)_fileTransfer.FileSizeInBytes;
                    var checkPercentComplete = 1 - checkPercentRemaining;
                    var changeSinceLastUpdate = checkPercentComplete - PercentComplete;

                    // this event fires on every file chunk sent event, which could be hurdreds of thousands
                    // of times depending on the file size and buffer size. Since this  event is only used
                    // by myself when debugging small test files, I limited this event to only fire when 
                    // the size of the file will result in 10 file chunk sent events at most.
                    if (_fileTransfer.FileSizeInBytes <= 10 * _bufferSize)
                    {
                        EventLog.Add(new ServerEvent
                        {
                            EventType = ServerEventType.SentFileChunkToRemoteServer,
                            FileSizeInBytes = _fileTransfer.FileSizeInBytes,
                            CurrentFileBytesSent = fileChunkSize,
                            FileBytesRemaining = BytesRemaining,
                            FileChunkSentCount = FileChunkSentCount,
                            SocketSendCount = socketSendCount,
                            FileTransferId = Id,
                            RequestId = RequestId
                        });

                        TestSocketEventOccurred?.Invoke(this, EventLog.Last());
                    }

                    // Report progress in intervals which are set by the user in the settings file
                    if (changeSinceLastUpdate < _updateInterval) continue;
                    PercentComplete = checkPercentComplete;

                    TestFileTransferProgress?.Invoke(this, new ServerEvent
                    {
                        EventType = ServerEventType.UpdateFileTransferProgress,
                        TotalFileBytesReceived = TotalBytesReceived,
                        PercentComplete = PercentComplete,
                        RequestId = RequestId,
                        FileTransferId = Id
                    });

                    if (PercentComplete < 0.20f) continue;

                    EventLog.Add(new ServerEvent
                    {
                        EventType = ServerEventType.StoppedSendingFileBytes,
                        RemoteServerIpAddress = _fileTransfer.RemoteServerIpAddress,
                        RemoteServerPortNumber = _fileTransfer.RemoteServerPortNumber,
                        RequestId = RequestId,
                        FileTransferId = Id,
                    });

                    TestEventOccurred?.Invoke(this, EventLog.Last());

                    return Result.Ok();
                }

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

                return Result.Ok();
            }
        }
    }
}
