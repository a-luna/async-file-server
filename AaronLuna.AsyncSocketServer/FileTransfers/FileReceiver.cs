using System;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using AaronLuna.AsyncSocketServer.SocketExtensions;
using AaronLuna.Common.IO;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncSocketServer.FileTransfers
{
    public class FileReceiver
    {
        static readonly object FileLock = new object();

        public FileReceiver(ServerSettings settings)
        {
            Settings = settings;
        }

        protected readonly ServerSettings Settings;

        public event EventHandler<ServerEvent> EventOccurred;
        public event EventHandler<ServerEvent> SocketEventOccurred;
        public event EventHandler<ServerEvent> FileTransferProgress;

        protected virtual void OnEventOccurred(ServerEvent serverEvent)
        {
            // Make a temporary copy of the event to avoid possibility of
            // a race condition if the last subscriber unsubscribes
            // immediately after the null check and before the event is raised.

            var handler = EventOccurred;
            handler?.Invoke(this, serverEvent);
        }

        public virtual async Task<Result> ReceiveFileAsync(
            FileTransfer fileTransfer,
            Socket socket,
            byte[] unreadBytes,
            CancellationToken token)
        {
            var bufferSize = Settings.SocketSettings.BufferSize;
            var timeoutMs = Settings.SocketSettings.SocketTimeoutInMilliseconds;
            var updateInterval = Settings.TransferUpdateInterval;
            var buffer = new byte[bufferSize];

            fileTransfer.Status = FileTransferStatus.InProgress;
            fileTransfer.TransferStartTime = DateTime.Now;

            EventOccurred?.Invoke(this, new ServerEvent
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

            var receiveCount = 0;
            fileTransfer.TotalBytesReceived = 0;
            fileTransfer.BytesRemaining = fileTransfer.FileSizeInBytes;
            fileTransfer.PercentComplete = 0;
            fileTransfer.InboundFileTransferStalled = false;

            if (unreadBytes.Length > 0)
            {
                fileTransfer.TotalBytesReceived += unreadBytes.Length;
                fileTransfer.BytesRemaining -= unreadBytes.Length;

                lock (FileLock)
                {
                    var writeBytesToFile =
                        FileHelper.WriteBytesToFile(
                            fileTransfer.LocalFilePath,
                            unreadBytes,
                            unreadBytes.Length,
                            10);

                    if (writeBytesToFile.Failure)
                    {
                        return writeBytesToFile;
                    }
                }

                EventOccurred?.Invoke(this, new ServerEvent
                {
                    EventType = EventType.CopySavedBytesToIncomingFile,
                    CurrentFileBytesReceived = unreadBytes.Length,
                    TotalFileBytesReceived = fileTransfer.TotalBytesReceived,
                    FileSizeInBytes = fileTransfer.FileSizeInBytes,
                    FileBytesRemaining = fileTransfer.BytesRemaining,
                    FileTransferId = fileTransfer.Id,
                });
            }

            // Read file bytes from transfer socket until
            //      1. the entire file has been received OR
            //      2. Data is no longer being received OR
            //      3, Transfer is canceled
            var receivedZeroBytesFromSocket = false;
            while (!receivedZeroBytesFromSocket)
            {
                if (token.IsCancellationRequested)
                {
                    fileTransfer.Status = FileTransferStatus.Cancelled;
                    fileTransfer.TransferCompleteTime = DateTime.Now;
                    fileTransfer.ErrorMessage = "Cancellation requested";

                    return Result.Ok();
                }

                var readFromSocket =
                    await socket.ReceiveWithTimeoutAsync(
                            buffer,
                            0,
                            bufferSize,
                            SocketFlags.None,
                            timeoutMs)
                        .ConfigureAwait(false);

                if (readFromSocket.Failure)
                {
                    return readFromSocket;
                }

                fileTransfer.CurrentBytesReceived = readFromSocket.Value;
                if (fileTransfer.CurrentBytesReceived == 0)
                {
                    receivedZeroBytesFromSocket = true;
                    continue;
                }

                var receivedBytes = new byte[fileTransfer.CurrentBytesReceived];
                buffer.ToList().CopyTo(0, receivedBytes, 0, fileTransfer.CurrentBytesReceived);

                int fileWriteAttempts;
                lock (FileLock)
                {
                    var writeBytesToFile = FileHelper.WriteBytesToFile(
                        fileTransfer.LocalFilePath,
                        receivedBytes,
                        fileTransfer.CurrentBytesReceived,
                        999);

                    if (writeBytesToFile.Failure)
                    {
                        return writeBytesToFile;
                    }

                    fileWriteAttempts = writeBytesToFile.Value + 1;
                }

                receiveCount++;
                fileTransfer.TotalBytesReceived += fileTransfer.CurrentBytesReceived;
                fileTransfer.BytesRemaining -= fileTransfer.CurrentBytesReceived;
                var checkPercentComplete = fileTransfer.TotalBytesReceived / (float)fileTransfer.FileSizeInBytes;
                var changeSinceLastUpdate = checkPercentComplete - fileTransfer.PercentComplete;

                if (fileWriteAttempts > 1)
                {
                    EventOccurred?.Invoke(this, new ServerEvent
                    {
                        EventType = EventType.MultipleFileWriteAttemptsNeeded,
                        FileWriteAttempts = fileWriteAttempts,
                        PercentComplete = fileTransfer.PercentComplete,
                        FileTransferId = fileTransfer.Id,
                    });
                }

                // this event fires on every socket read event, which could be hurdreds of thousands
                // of times depending on the file size and buffer size. Since this  event is only used
                // by myself when debugging small test files, I limited this event to only fire when
                // the size of the file will result in 10 socket read events at most.
                if (fileTransfer.FileSizeInBytes <= 10 * bufferSize)
                {
                    SocketEventOccurred?.Invoke(this, new ServerEvent
                    {
                        EventType = EventType.ReceivedFileBytesFromSocket,
                        SocketReadCount = receiveCount,
                        BytesReceivedCount = fileTransfer.CurrentBytesReceived,
                        CurrentFileBytesReceived = fileTransfer.CurrentBytesReceived,
                        TotalFileBytesReceived = fileTransfer.TotalBytesReceived,
                        FileSizeInBytes = fileTransfer.FileSizeInBytes,
                        FileBytesRemaining = fileTransfer.BytesRemaining,
                        PercentComplete = fileTransfer.PercentComplete,
                        FileTransferId = fileTransfer.Id
                    });
                }

                // Report progress in intervals which are set by the user in the settings file
                if (changeSinceLastUpdate < updateInterval) continue;

                fileTransfer.PercentComplete = checkPercentComplete;

                FileTransferProgress?.Invoke(this, new ServerEvent
                {
                    EventType = EventType.UpdateFileTransferProgress,
                    TotalFileBytesReceived = fileTransfer.TotalBytesReceived,
                    PercentComplete = fileTransfer.PercentComplete,
                    FileTransferId = fileTransfer.Id
                });
            }

            var fileTransferIsIncomplete = (fileTransfer.TotalBytesReceived / (float)fileTransfer.FileSizeInBytes) < 1;
            if (fileTransfer.InboundFileTransferStalled || fileTransferIsIncomplete)
            {
                const string fileTransferStalledErrorMessage =
                    "Data is no longer bring received from remote client, file transfer has been canceled (ReceiveFileAsync)";

                fileTransfer.Status = FileTransferStatus.Stalled;
                fileTransfer.ErrorMessage = fileTransferStalledErrorMessage;

                return Result.Ok();
            }

            fileTransfer.Status = FileTransferStatus.ConfirmedComplete;
            fileTransfer.TransferCompleteTime = DateTime.Now;
            fileTransfer.PercentComplete = 1;
            fileTransfer.CurrentBytesReceived = 0;
            fileTransfer.BytesRemaining = 0;

            EventOccurred?.Invoke(this, new ServerEvent
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

            return Result.Ok();
        }
    }
}