namespace AaronLuna.AsyncFileServer.Controller
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net.Sockets;
    using System.Threading;
    using System.Threading.Tasks;

    using Common.Extensions;
    using Common.IO;
    using Common.Logging;
    using Common.Result;

    using Model;
    using Utilities;

    public class FileTransferController
    {
        readonly Logger _log = new Logger(typeof(FileTransferController));
        readonly int _bufferSize;
        readonly int _timeoutMs;
        readonly float _updateInterval;
        byte[] _buffer;
        Socket _socket;
        FileTransfer _fileTransfer;
        static readonly object FileLock = new object();

        public FileTransferController(int id, ServerSettings settings)
        {
            Id = id;
            EventLog = new List<ServerEvent>();
            RemoteServerInfo = new ServerInfo();
            LocalServerInfo = new ServerInfo();

            TransferDirection = FileTransferDirection.None;
            Initiator = FileTransferInitiator.None;
            Status = FileTransferStatus.None;

            RequestInitiatedTime = DateTime.MinValue;
            TransferStartTime = DateTime.MinValue;
            TransferCompleteTime = DateTime.MinValue;
            RetryLockoutExpireTime = DateTime.MinValue;

            ErrorMessage = string.Empty;

            RemoteServerRetryLimit = settings.TransferRetryLimit;
            RetryCounter = 1;
            RemoteServerTransferId = 0;
            TransferResponseCode = 0;

            _bufferSize = settings.SocketSettings.BufferSize;
            _timeoutMs = settings.SocketSettings.SocketTimeoutInMilliseconds;
            _updateInterval = settings.TransferUpdateInterval;
            _buffer = new byte[_bufferSize];
            _fileTransfer = new FileTransfer();
        }

        public int Id { get; }
        public int RemoteServerTransferId { get; set; }
        public long TransferResponseCode { get; set; }
        public FileTransferDirection TransferDirection { get; set; }
        public FileTransferInitiator Initiator { get; set; }
        public FileTransferStatus Status { get; set; }
        public bool OutboundFileTransferStalled { get; set; }
        public bool InboundFileTransferStalled { get; set; }
        public string ErrorMessage { get; set; }
        public List<ServerEvent> EventLog { get; set; }
        public ServerInfo RemoteServerInfo { get; set; }
        public ServerInfo LocalServerInfo { get; set; }
        public int RequestId { get; set; }

        public long FileSizeInBytes
        {
            get => _fileTransfer.FileSizeInBytes;
            set => _fileTransfer.FileSizeInBytes = value;
        }
        public string FileSizeString => FileHelper.FileSizeToString(FileSizeInBytes);

        public string LocalFilePath => _fileTransfer.LocalFilePath;
        public string LocalFolderPath => _fileTransfer.LocalFolderPath;
        public string RemoteFilePath => _fileTransfer.RemoteFilePath;
        public string RemoteFolderPath => _fileTransfer.RemoteFolderPath;
        public string FileName => _fileTransfer.FileName;

        public DateTime RequestInitiatedTime { get; set; }
        public DateTime TransferStartTime { get; set; }
        public DateTime TransferCompleteTime { get; set; }
        public TimeSpan TransferTimeSpan => TransferCompleteTime - TransferStartTime;
        public string TransferTimeElapsed => TransferTimeSpan.ToFormattedString();

        public int RetryCounter { get; set; }
        public int RemoteServerRetryLimit { get; set; }
        public DateTime RetryLockoutExpireTime { get; set; }
        public string RetryLockoutTimeRemianing => (RetryLockoutExpireTime - DateTime.Now).ToFormattedString();
        public bool RetryLockoutExpired => RetryLockoutExpireTime < DateTime.Now;

        public int CurrentBytesReceived { get; set; }
        public long TotalBytesReceived { get; set; }
        public int CurrentBytesSent { get; set; }
        public long BytesRemaining { get; set; }
        public int FileChunkSentCount { get; set; }
        public float PercentComplete { get; set; }
        public string TransferRate => GetTransferRate(TransferTimeSpan, _fileTransfer.FileSizeInBytes);

        public bool AwaitingResponse => Status == FileTransferStatus.Pending;
        public bool TransferStalled => Status == FileTransferStatus.Stalled;
        public bool TransferNeverStarted => Status.TransferNeverStarted();
        public bool TransferStartedButDidNotComplete => Status.TransferStartedButDidNotComplete();
        public bool TransfercompletedSucessfully => Status.TransfercompletedSucessfully();

        public event EventHandler<ServerEvent> EventOccurred;
        public event EventHandler<ServerEvent> SocketEventOccurred;
        public event EventHandler<ServerEvent> FileTransferProgress;

        public virtual void Initialize(
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

            if (TransferDirection == FileTransferDirection.Outbound)
            {
                TransferResponseCode = DateTime.Now.Ticks;
            }

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

        public void ResetTransferValues()
        {
            RetryCounter++;
            Status = FileTransferStatus.Pending;
            ErrorMessage = string.Empty;

            RequestInitiatedTime = DateTime.MinValue;
            TransferStartTime = DateTime.MinValue;
            TransferCompleteTime = DateTime.MinValue;
            
            CurrentBytesSent = 0;
            FileChunkSentCount = 0;
            PercentComplete = 0;
        }

        public virtual async Task<Result> SendFileAsync(Socket socket, CancellationToken token)
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

            EventOccurred?.Invoke(this, EventLog.Last());

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

                    var fileChunkSize = (int) Math.Min(_bufferSize, BytesRemaining);
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

                        SocketEventOccurred?.Invoke(this, EventLog.Last());
                    }
                    
                    // Report progress in intervals which are set by the user in the settings file
                    if (changeSinceLastUpdate < _updateInterval) continue;
                    PercentComplete = checkPercentComplete;

                    FileTransferProgress?.Invoke(this, new ServerEvent
                    {
                        EventType = ServerEventType.UpdateFileTransferProgress,
                        TotalFileBytesReceived = TotalBytesReceived,
                        PercentComplete = PercentComplete,
                        RequestId = RequestId,
                        FileTransferId = Id
                    });
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

                EventOccurred?.Invoke(this, EventLog.Last());

                return Result.Ok();
            }
        }

        public virtual async Task<Result> ReceiveFileAsync(
            Socket socket,
            byte[] unreadBytes,
            CancellationToken token)
        {
            _socket = socket;
            Status = FileTransferStatus.InProgress;
            TransferStartTime = DateTime.Now;

            EventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceiveFileBytesStarted,
                FileTransferId = Id,
                RemoteServerIpAddress = _fileTransfer.RemoteServerIpAddress,
                RemoteServerPortNumber = _fileTransfer.RemoteServerPortNumber,
                FileTransferStartTime = TransferStartTime,
                FileName = FileName,
                FileSizeInBytes = FileSizeInBytes,
                LocalFolder = LocalFolderPath,
                RetryCounter = RetryCounter,
                RemoteServerRetryLimit = RemoteServerRetryLimit,
                RequestId = RequestId
            });

            EventOccurred?.Invoke(this, EventLog.Last());

            var receiveCount = 0;
            TotalBytesReceived = 0;
            BytesRemaining = FileSizeInBytes;
            PercentComplete = 0;
            InboundFileTransferStalled = false;

            if (unreadBytes.Length > 0)
            {
                TotalBytesReceived += unreadBytes.Length;
                BytesRemaining -= unreadBytes.Length;

                lock (FileLock)
                {
                    var writeBytesToFile =
                        FileHelper.WriteBytesToFile(
                            LocalFilePath,
                            unreadBytes,
                            unreadBytes.Length,
                            10);

                    if (writeBytesToFile.Failure)
                    {
                        return writeBytesToFile;
                    }
                }

                EventLog.Add(new ServerEvent
                {
                    EventType = ServerEventType.CopySavedBytesToIncomingFile,
                    CurrentFileBytesReceived = unreadBytes.Length,
                    TotalFileBytesReceived = TotalBytesReceived,
                    FileSizeInBytes = FileSizeInBytes,
                    FileBytesRemaining = BytesRemaining,
                    RequestId = RequestId,
                    FileTransferId = Id,
                });

                EventOccurred?.Invoke(this, EventLog.Last());
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
                    Status = FileTransferStatus.Cancelled;
                    TransferCompleteTime = DateTime.Now;
                    ErrorMessage = "Cancellation requested";

                    return Result.Ok();
                }

                var readFromSocket =
                    await _socket.ReceiveWithTimeoutAsync(
                            _buffer,
                            0,
                            _bufferSize,
                            SocketFlags.None,
                            _timeoutMs)
                        .ConfigureAwait(false);

                if (readFromSocket.Failure)
                {
                    return readFromSocket;
                }

                CurrentBytesReceived = readFromSocket.Value;
                if (CurrentBytesReceived == 0)
                {
                    receivedZeroBytesFromSocket = true;
                    continue;
                }

                var receivedBytes = new byte[CurrentBytesReceived];
                _buffer.ToList().CopyTo(0, receivedBytes, 0, CurrentBytesReceived);

                int fileWriteAttempts;
                lock (FileLock)
                {
                    var writeBytesToFile = FileHelper.WriteBytesToFile(
                        LocalFilePath,
                        receivedBytes,
                        CurrentBytesReceived,
                        999);

                    if (writeBytesToFile.Failure)
                    {
                        return writeBytesToFile;
                    }

                    fileWriteAttempts = writeBytesToFile.Value + 1;
                }

                receiveCount++;
                TotalBytesReceived += CurrentBytesReceived;
                BytesRemaining -= CurrentBytesReceived;
                var checkPercentComplete = TotalBytesReceived / (float) FileSizeInBytes;
                var changeSinceLastUpdate = checkPercentComplete - PercentComplete;

                if (fileWriteAttempts > 1)
                {
                    EventLog.Add(new ServerEvent
                    {
                        EventType = ServerEventType.MultipleFileWriteAttemptsNeeded,
                        FileWriteAttempts = fileWriteAttempts,
                        PercentComplete = PercentComplete,
                        RequestId = RequestId,
                        FileTransferId = Id,
                    });

                    EventOccurred?.Invoke(this, EventLog.Last());
                }

                // this event fires on every socket read event, which could be hurdreds of thousands
                // of times depending on the file size and buffer size. Since this  event is only used
                // by myself when debugging small test files, I limited this event to only fire when 
                // the size of the file will result in 10 socket read events at most.
                if (FileSizeInBytes <= 10 * _bufferSize)
                {
                    EventLog.Add(new ServerEvent
                    {
                        EventType = ServerEventType.ReceivedFileBytesFromSocket,
                        SocketReadCount = receiveCount,
                        BytesReceivedCount = CurrentBytesReceived,
                        CurrentFileBytesReceived = CurrentBytesReceived,
                        TotalFileBytesReceived = TotalBytesReceived,
                        FileSizeInBytes = FileSizeInBytes,
                        FileBytesRemaining = BytesRemaining,
                        PercentComplete = PercentComplete,
                        RequestId = RequestId,
                        FileTransferId = Id
                    });

                    SocketEventOccurred?.Invoke(this, EventLog.Last());
                }

                // Report progress in intervals which are set by the user in the settings file
                if (changeSinceLastUpdate < _updateInterval) continue;

                PercentComplete = checkPercentComplete;

                FileTransferProgress?.Invoke(this, new ServerEvent
                {
                    EventType = ServerEventType.UpdateFileTransferProgress,
                    TotalFileBytesReceived = TotalBytesReceived,
                    PercentComplete = PercentComplete,
                    RequestId = RequestId,
                    FileTransferId = Id
                });
            }

            var fileTransferIsIncomplete = (TotalBytesReceived / (float) FileSizeInBytes) < 1;
            if (InboundFileTransferStalled || fileTransferIsIncomplete)
            {
                const string fileTransferStalledErrorMessage =
                    "Data is no longer bring received from remote client, file transfer has been canceled (ReceiveFileAsync)";

                Status = FileTransferStatus.Stalled;
                ErrorMessage = fileTransferStalledErrorMessage;

                return Result.Ok();
            }

            Status = FileTransferStatus.ConfirmedComplete;
            TransferCompleteTime = DateTime.Now;
            PercentComplete = 1;
            CurrentBytesReceived = 0;
            BytesRemaining = 0;

            EventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.ReceiveFileBytesComplete,
                FileTransferStartTime = TransferStartTime,
                FileTransferCompleteTime = DateTime.Now,
                FileSizeInBytes = FileSizeInBytes,
                FileTransferRate = TransferRate,
                RemoteServerIpAddress = _fileTransfer.RemoteServerIpAddress,
                RemoteServerPortNumber = _fileTransfer.RemoteServerPortNumber,
                RequestId = RequestId,
                FileTransferId = Id
            });

            EventOccurred?.Invoke(this, EventLog.Last());

            return Result.Ok();
        }

        public override string ToString()
        {
            var retryLimit = RemoteServerRetryLimit == 0
                ? string.Empty
                : $"/{RemoteServerRetryLimit}{Environment.NewLine}";

            var attempt = $"Attempt #{RetryCounter}{retryLimit}";

            var initiator = Initiator == FileTransferInitiator.RemoteServer
                ? "Remote Server"
                : "Me";

            var transferStatus = $"[{Status.Name()}] {attempt}";
            var requestType = $"   {TransferDirection} file transfer initiated by {initiator}";
            var fileName = $"   File Name..........: {_fileTransfer.FileName}";
            var fileSize = $"   File Size..........: {_fileTransfer.FileSizeString}";
            var remoteServerInfo = $"   Remote Server......: {RemoteServerInfo}";

            var requestInitiatedTime = Initiator == FileTransferInitiator.Self
                ? $"   Request Sent.......: {RequestInitiatedTime:MM/dd/yyyy hh:mm:ss.fff tt}{Environment.NewLine}"
                : $"   Request Received...: {RequestInitiatedTime:MM/dd/yyyy hh:mm:ss.fff tt}{Environment.NewLine}";

            var transferStartTime =
                $"   Transfer Started...: {TransferStartTime:MM/dd/yyyy hh:mm:ss.fff tt}{Environment.NewLine}";

            var transferCompleteTime =                
                $"   Transfer Complete..: {TransferCompleteTime:MM/dd/yyyy hh:mm:ss.fff tt}{Environment.NewLine}";

            var lockoutExpireTime =
                $"   Lockout Expires....: {RetryLockoutExpireTime:MM/dd/yyyy hh:mm:ss.fff tt}{Environment.NewLine}";

            var timeStamps = string.Empty;
            if (Status == FileTransferStatus.RetryLimitExceeded)
            {
                timeStamps = requestInitiatedTime + lockoutExpireTime;
            }
            else if (TransferNeverStarted)
            {
                timeStamps = requestInitiatedTime;
            }
            else if (TransferStartedButDidNotComplete)
            {
                timeStamps = requestInitiatedTime + transferStartTime;
            }
            else if (TransfercompletedSucessfully)
            {
                timeStamps = requestInitiatedTime + transferStartTime + transferCompleteTime;
            }

            return
                transferStatus + Environment.NewLine +
                requestType + Environment.NewLine + Environment.NewLine +
                fileName + Environment.NewLine +
                fileSize + Environment.NewLine +
                remoteServerInfo + Environment.NewLine +
                timeStamps;
        }

        public string TransferDetails()
        {
            var requestInitiated = RequestInitiatedTime != DateTime.MinValue
                ? $"{RequestInitiatedTime:MM/dd/yyyy hh:mm:ss.fff tt}"
                : string.Empty;

            var transferStarted = TransferStartTime != DateTime.MinValue
                ? $"{TransferStartTime:MM/dd/yyyy hh:mm:ss.fff tt}"
                : string.Empty;

            var transferComplete = TransferCompleteTime != DateTime.MinValue
                ? $"{TransferCompleteTime:MM/dd/yyyy hh:mm:ss.fff tt}"
                : string.Empty;

            var timeElapsed = TransferStartTime != DateTime.MinValue && TransferCompleteTime != DateTime.MinValue
                ? TransferTimeElapsed
                : string.Empty;

            var lockoutExpireTime = RetryLockoutExpireTime != DateTime.MinValue
                ? $"{RetryLockoutExpireTime:MM/dd/yyyy hh:mm:ss.fff tt}"
                : string.Empty;

            var transferLimitExceededString = !RetryLockoutExpired
                ? "FALSE"
                : "TRUE";

            var transferLimitExceeded = RetryLockoutExpireTime != DateTime.MinValue
                ? transferLimitExceededString
                : string.Empty;

            var retryLockout = string.Empty;
            retryLockout += $" Retry Limit Exceeded...: {transferLimitExceeded}{Environment.NewLine}";
            retryLockout += $" Retry Lockout Expires..: {lockoutExpireTime}{Environment.NewLine}";

            var details = string.Empty;
            details += $" ID.....................: {Id}{Environment.NewLine}";
            details += $" Remote Server ID.......: {RemoteServerTransferId}{Environment.NewLine}";
            details += $" Response Code..........: {TransferResponseCode}{Environment.NewLine}";
            details += $" Initiator..............: {Initiator}{Environment.NewLine}";
            details += $" Direction..............: {TransferDirection}{Environment.NewLine}";
            details += $" Status.................: {Status}{Environment.NewLine}{Environment.NewLine}";

            details += $" File Name..............: {_fileTransfer.FileName}{Environment.NewLine}";
            details += $" File Size..............: {_fileTransfer.FileSizeString} ({_fileTransfer.FileSizeInBytes:N0} bytes){Environment.NewLine}";
            details += $" Local Folder...........: {_fileTransfer.LocalFolderPath}{Environment.NewLine}";

            var remoteFolder = $" Remote Folder..........: {_fileTransfer.RemoteFolderPath}{Environment.NewLine}{Environment.NewLine}";

            details += string.IsNullOrEmpty(_fileTransfer.RemoteFolderPath)
                ? Environment.NewLine
                : remoteFolder;

            var direction = string.Empty;
            if (Initiator == FileTransferInitiator.Self)
            {
                direction = "Sent...........";
            }

            if (Initiator == FileTransferInitiator.RemoteServer)
            {
                direction = "Received.......";
            }

            details += $" Request {direction}: {requestInitiated}{Environment.NewLine}";

            if (Status == FileTransferStatus.Pending) return details;

            details += $" Transfer Started.......: {transferStarted}{Environment.NewLine}";
            details += $" Transfer Completed.....: {transferComplete}{Environment.NewLine}";

            if (Status == FileTransferStatus.Rejected)
            {
                details += string.IsNullOrEmpty(ErrorMessage)
                    ? string.Empty
                    : $" Error Message..........: {ErrorMessage}{Environment.NewLine}";

                return details;
            }

            details += $" Transfer Time Elapsed..: {timeElapsed}{Environment.NewLine}";
            details += $" Transfer Rate..........: {TransferRate}{Environment.NewLine}{Environment.NewLine}";

            details += $" Percent Complete.......: {PercentComplete:P2}{Environment.NewLine}";

            var bytesReceived = string.Empty;
            bytesReceived += $" Total Bytes Received...: {TotalBytesReceived:N0}{Environment.NewLine}";
            bytesReceived += $" Current Bytes Received : {CurrentBytesReceived:N0}{Environment.NewLine}";
            bytesReceived += $" Bytes Remaining........: {BytesRemaining:N0}{Environment.NewLine}{Environment.NewLine}";

            var bytesSent = string.Empty;
            bytesSent += $" File Chunks Sent.......: {FileChunkSentCount:N0}{Environment.NewLine}";
            bytesSent += $" Current Bytes Sent.....: {CurrentBytesSent:N0}{Environment.NewLine}";
            bytesSent += $" Bytes Remaining........: {BytesRemaining:N0}{Environment.NewLine}{Environment.NewLine}";

            details += TransferDirection == FileTransferDirection.Inbound
                ? bytesReceived
                : bytesSent;

            details += $" Transfer Attempt #.....: {RetryCounter}{Environment.NewLine}";

            details += string.IsNullOrEmpty(ErrorMessage)
                ? string.Empty
                : $" Error Message..........: {ErrorMessage}{Environment.NewLine}";

            details += $" Max Download Attempts..: {RemoteServerRetryLimit}{Environment.NewLine}";

            details += RetryLockoutExpireTime != DateTime.MinValue
                ? retryLockout
                : string.Empty;

            return details;
        }

        public string InboundRequestDetails(bool addHeader)
        {
            var remoteServerIp = RemoteServerInfo.SessionIpAddress;
            var remotePortNumber = RemoteServerInfo.PortNumber;
            var retryCounter = RetryCounter;
            var remoteServerRetryLimit = RemoteServerRetryLimit;
            var fileName = FileName;
            var fileSize = FileSizeString;
            var localFolder = LocalFolderPath;

            var retryLimit = remoteServerRetryLimit == 0
                ? string.Empty
                : $"/{remoteServerRetryLimit}{Environment.NewLine}";

            var transferAttempt = $"Attempt #{retryCounter}{retryLimit}";

            var header =
                $"Inbound file transfer request ({transferAttempt})" +
                Environment.NewLine + Environment.NewLine;

            var fileInfo =
                $"File Sender..: {remoteServerIp}:{remotePortNumber}{Environment.NewLine}" +
                $"File Name....: {fileName}{Environment.NewLine}" +
                $"File Size....: {fileSize}{Environment.NewLine}" +
                $"Save To......: {localFolder}{Environment.NewLine}";

            return addHeader
                ? header + fileInfo
                : fileInfo;
        }

        public string OutboundRequestDetails()
        {
            var remoteServerIp = RemoteServerInfo.SessionIpAddress;
            var remotePortNumber = RemoteServerInfo.PortNumber;

            return
                $"Send File To...: {remoteServerIp}:{remotePortNumber}{Environment.NewLine}" +
                $"File Name......: {FileName}{Environment.NewLine}" +
                $"File Size......: {FileSizeInBytes:N0} bytes ({FileSizeString}){Environment.NewLine}" +
                $"File Location..: {LocalFolderPath}{Environment.NewLine}" +
                $"Target Folder..: {RemoteFolderPath}{Environment.NewLine}";
        }

        public static string GetTransferRate(TimeSpan elapsed, long bytesReceived)
        {
            if (elapsed == TimeSpan.MinValue || bytesReceived == 0)
            {
                return string.Empty;
            }

            var elapsedMilliseconds = elapsed.Ticks / (double)10_000;
            var bytesPerSecond = (bytesReceived * 1000) / elapsedMilliseconds;
            var kilobytesPerSecond = bytesPerSecond / 1024;
            var megabytesPerSecond = kilobytesPerSecond / 1024;

            if (megabytesPerSecond > 1)
            {
                return $"{megabytesPerSecond:F1} MB/s";
            }

            return kilobytesPerSecond > 1
                ? $"{kilobytesPerSecond:F1} KB/s"
                : $"{bytesPerSecond:F1} bytes/s";
        }
    }
}
