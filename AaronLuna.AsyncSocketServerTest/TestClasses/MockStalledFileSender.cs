using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using AaronLuna.AsyncSocketServer;
using AaronLuna.AsyncSocketServer.FileTransfers;
using AaronLuna.AsyncSocketServer.SocketExtensions;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncSocketServerTest.TestClasses
{
    public class MockStalledFileSender : FileSender
    {
        public MockStalledFileSender(ServerSettings settings) :base(settings) { }

        public override async Task<Result> SendFileAsync(
            FileTransfer fileTransfer,
            Socket socket,
            CancellationToken token)
        {
            var bufferSize = Settings.SocketSettings.BufferSize;
            var timeoutMs = Settings.SocketSettings.SocketTimeoutInMilliseconds;
            var updateInterval = Settings.TransferUpdateInterval;

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

            using (var file = File.OpenRead(fileTransfer.LocalFilePath))
            {
                while (fileTransfer.BytesRemaining > 0)
                {
                    if (token.IsCancellationRequested)
                    {
                        fileTransfer.Status = FileTransferStatus.Cancelled;
                        fileTransfer.TransferCompleteTime = DateTime.Now;
                        fileTransfer.ErrorMessage = "Cancellation requested";

                        return Result.Ok();
                    }

                    var fileChunkSize = (int)Math.Min(bufferSize, fileTransfer.BytesRemaining);
                    var buffer = new byte[fileChunkSize];

                    var numberOfBytesToSend = file.Read(buffer, 0, fileChunkSize);
                    fileTransfer.BytesRemaining -= numberOfBytesToSend;

                    var offset = 0;
                    var socketSendCount = 0;
                    while (numberOfBytesToSend > 0)
                    {
                        var sendFileChunkResult =
                            await socket.SendWithTimeoutAsync(
                                buffer,
                                offset,
                                fileChunkSize,
                                SocketFlags.None,
                                timeoutMs).ConfigureAwait(false);

                        if (fileTransfer.OutboundFileTransferStalled)
                        {
                            const string fileTransferStalledErrorMessage =
                                "Aborting file transfer, client says that data is no longer being received (SendFileBytesAsync)";

                            fileTransfer.Status = FileTransferStatus.Cancelled;
                            fileTransfer.TransferCompleteTime = DateTime.Now;
                            fileTransfer.ErrorMessage = fileTransferStalledErrorMessage;

                            return Result.Ok();
                        }

                        if (sendFileChunkResult.Failure)
                        {
                            return sendFileChunkResult;
                        }

                        fileTransfer.CurrentBytesSent = sendFileChunkResult.Value;
                        numberOfBytesToSend -= fileTransfer.CurrentBytesSent;
                        offset += fileTransfer.CurrentBytesSent;
                        socketSendCount++;
                    }

                    fileTransfer.CurrentBytesSent = fileChunkSize;
                    fileTransfer.FileChunkSentCount++;

                    var checkPercentRemaining = fileTransfer.BytesRemaining / (float) fileTransfer.FileSizeInBytes;
                    var checkPercentComplete = 1 - checkPercentRemaining;
                    var changeSinceLastUpdate = checkPercentComplete - fileTransfer.PercentComplete;

                    // this event fires on every file chunk sent event, which could be hurdreds of thousands
                    // of times depending on the file size and buffer size. Since this  event is only used
                    // by myself when debugging small test files, I limited this event to only fire when
                    // the size of the file will result in 10 file chunk sent events at most.
                    if (fileTransfer.FileSizeInBytes <= 10 * bufferSize)
                    {
                        OnSocketEventOccurred(new ServerEvent
                        {
                            EventType = EventType.SentFileChunkToRemoteServer,
                            FileSizeInBytes = fileTransfer.FileSizeInBytes,
                            CurrentFileBytesSent = fileChunkSize,
                            FileBytesRemaining = fileTransfer.BytesRemaining,
                            FileChunkSentCount = fileTransfer.FileChunkSentCount,
                            SocketSendCount = socketSendCount,
                            FileTransferId = fileTransfer.Id
                        });
                    }

                    // Report progress in intervals which are set by the user in the settings file
                    if (changeSinceLastUpdate < updateInterval) continue;
                    fileTransfer.PercentComplete = checkPercentComplete;

                    OnFileTransferProgress(new ServerEvent
                    {
                        EventType = EventType.UpdateFileTransferProgress,
                        TotalFileBytesReceived = fileTransfer.TotalBytesReceived,
                        PercentComplete = fileTransfer.PercentComplete,
                        FileTransferId = fileTransfer.Id
                    });

                    if (fileTransfer.PercentComplete < 0.20f) continue;

                    OnEventOccurred(new ServerEvent
                    {
                        EventType = EventType.StoppedSendingFileBytes,
                        RemoteServerIpAddress = fileTransfer.RemoteServerInfo.SessionIpAddress,
                        RemoteServerPortNumber = fileTransfer.RemoteServerInfo.PortNumber,
                        FileTransferId = fileTransfer.Id,
                    });

                    return Result.Ok();
                }

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
                    FileTransferId = fileTransfer.Id,
                });

                return Result.Ok();
            }
        }
    }
}
