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

        public FileTransferController(int id, int bufferSize, int timeoutMs, float updateInterval)
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

            RetryCounter = 1;
            RemoteServerTransferId = 0;
            TransferResponseCode = 0;

            _bufferSize = bufferSize;
            _timeoutMs = timeoutMs;
            _updateInterval = updateInterval;
            _buffer = new byte[bufferSize];
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

        public string LocalFilePath => _fileTransfer.LocalFilePath;
        public string LocalFolderPath => _fileTransfer.LocalFolderPath;
        public string RemoteFilePath => _fileTransfer.RemoteFilePath;
        public string RemoteFolderPath => _fileTransfer.RemoteFolderPath;
        public string FileName => GetFileName();
        
        public DateTime RequestInitiatedTime { get; set; }
        public DateTime TransferStartTime { get; set; }
        public DateTime TransferCompleteTime { get; set; }
        public TimeSpan TransferTimeSpan => TransferCompleteTime - TransferStartTime;
        public string TransferTimeElapsed => TransferTimeSpan.ToFormattedString();

        public int RetryCounter { get; set; }
        public int RemoteServerRetryLimit { get; set; }
        public DateTime RetryLockoutExpireTime { get; set; }
        public string RetryLockoutTimeRemianing => (RetryLockoutExpireTime - DateTime.Now).ToFormattedString();
        public bool RetryLockoutExpired => RetryLockoutExpireTime > DateTime.Now;

        public int CurrentBytesReceived { get; set; }
        public long TotalBytesReceived { get; set; }
        public int CurrentBytesSent { get; set; }
        public long BytesRemaining { get; set; }
        public int FileChunkSentCount { get; set; }
        public float PercentComplete { get; set; }
        public string TransferRate => GetTransferRate(TransferTimeSpan, _fileTransfer.FileSizeInBytes);

        public bool AwaitingResponse => Status == FileTransferStatus.AwaitingResponse;
        public bool TransferStalled => Status == FileTransferStatus.Stalled;
        public bool TasksRemaining => Status.TasksRemaining();

        public event EventHandler<ServerEvent> EventOccurred;
        public event EventHandler<ServerEvent> SocketEventOccurred;
        public event EventHandler<ServerEvent> FileTransferProgress;

        public void InitializeOutboundFileTransfer(
            FileTransferInitiator initiator,
            ServerInfo localServerInfo,
            ServerInfo remoteServerInfo,
            string localFilePath,
            string remoteFolderPath)
        {
            LocalServerInfo = localServerInfo;
            RemoteServerInfo = remoteServerInfo;
            TransferDirection = FileTransferDirection.Outbound;
            Initiator = initiator;
            Status = FileTransferStatus.AwaitingResponse;
            TransferResponseCode = DateTime.Now.Ticks;
            RequestInitiatedTime = DateTime.Now;

            _fileTransfer = new FileTransfer
            {
                MyLocalIpAddress = LocalServerInfo.LocalIpAddress,
                MyPublicIpAddress = LocalServerInfo.PublicIpAddress,
                MyServerPortNumber = LocalServerInfo.PortNumber,
                RemoteServerIpAddress = RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = RemoteServerInfo.PortNumber,
                RemoteServerName = RemoteServerInfo.Name,
                LocalFilePath = localFilePath,
                LocalFolderPath = Path.GetDirectoryName(localFilePath),
                RemoteFolderPath = remoteFolderPath,
                RemoteFilePath = Path.Combine(remoteFolderPath, Path.GetFileName(localFilePath)),
                FileSizeInBytes = new FileInfo(localFilePath).Length
            };
        }

        public void InitializeInboundFileTransfer(
            FileTransferInitiator initiator,
            ServerInfo localServerInfo,
            ServerInfo remoteServerInfo,
            string fileName,
            long fileSizeBytes,
            string localFolderPath,
            string remoteFolderPath)
        {
            LocalServerInfo = localServerInfo;
            RemoteServerInfo = remoteServerInfo;
            TransferDirection = FileTransferDirection.Inbound;
            Initiator = initiator;
            Status = FileTransferStatus.AwaitingResponse;
            RequestInitiatedTime = DateTime.Now;

            _fileTransfer = new FileTransfer
            {
                MyLocalIpAddress = LocalServerInfo.LocalIpAddress,
                MyPublicIpAddress = LocalServerInfo.PublicIpAddress,
                MyServerPortNumber = LocalServerInfo.PortNumber,
                RemoteServerIpAddress = RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = RemoteServerInfo.PortNumber,
                RemoteFilePath = Path.Combine(remoteFolderPath, fileName),
                RemoteFolderPath = remoteFolderPath,
                LocalFilePath = Path.Combine(localFolderPath, fileName),
                LocalFolderPath = localFolderPath,
                FileSizeInBytes = fileSizeBytes
            };
        }
        
        public void ResetTransferValues()
        {
            RetryCounter++;
            Status = FileTransferStatus.AwaitingResponse;
            ErrorMessage = string.Empty;

            RequestInitiatedTime = DateTime.MinValue;
            TransferStartTime = DateTime.MinValue;
            TransferCompleteTime = DateTime.MinValue;

            CurrentBytesReceived = 0;
            TotalBytesReceived = 0;
            CurrentBytesSent = 0;
            BytesRemaining = 0;
            FileChunkSentCount = 0;
            PercentComplete = 0;
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

            var transferStatus = $"[{Status}] {attempt}{Environment.NewLine}";
            var requestType = $"{TransferDirection} file transfer initiated by {initiator}{Environment.NewLine}{Environment.NewLine}";
            var fileName = $"   File Name........: {_fileTransfer.FileName}{Environment.NewLine}";
            var fileSize = $"   File Size........: {_fileTransfer.FileSizeString}{Environment.NewLine}";
            var remoteServerInfo = $"   Remote Server....: {RemoteServerInfo}{Environment.NewLine}";

            var requestTime = Initiator == FileTransferInitiator.Self
                ? $"   Request Sent.....: {RequestInitiatedTime:MM/dd/yyyy hh:mm:ss.fff tt}{Environment.NewLine}"
                : $"   Request Received : {RequestInitiatedTime:MM/dd/yyyy hh:mm:ss.fff tt}{Environment.NewLine}";

            var transferTime = Status.TasksRemaining()
                ? $"   Transfer Started : {TransferStartTime:MM/dd/yyyy hh:mm:ss.fff tt}{Environment.NewLine}"
                : $"   Transfer Complete: {TransferCompleteTime:MM/dd/yyyy hh:mm:ss.fff tt}{Environment.NewLine}";

            var lockoutExpireTime = Status == FileTransferStatus.RetryLimitExceeded
                ? $"   Lockout Expires..: {RetryLockoutExpireTime:MM/dd/yyyy hh:mm:ss.fff tt}{Environment.NewLine}"
                : transferTime;

            var summaryNotStarted = requestType + fileName + fileSize + remoteServerInfo + requestTime;

            var summaryStarted = $"{transferStatus}   {summaryNotStarted}{lockoutExpireTime}";

            return Status == FileTransferStatus.AwaitingResponse
                ? summaryNotStarted
                : summaryStarted;
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

            details += $" Request Initiated......: {requestInitiated}{Environment.NewLine}";

            if (Status == FileTransferStatus.AwaitingResponse) return details;

            details += $" Transfer Started.......: {transferStarted}{Environment.NewLine}";
            details += $" Transfer Completed.....: {transferComplete}{Environment.NewLine}";

            if (Status == FileTransferStatus.Rejected) return details;

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

        public async Task<Result> SendFileBytesAsync(Socket socket, CancellationToken token)
        {
            _socket = socket;
            Status = FileTransferStatus.InProgress;
            TransferStartTime = DateTime.Now;

            EventLog.Add(new ServerEvent
            {
                EventType = ServerEventType.SendFileBytesStarted,
                RemoteServerIpAddress = _fileTransfer.RemoteServerIpAddress,
                RemoteServerPortNumber = _fileTransfer.RemoteServerPortNumber,
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

                    FileChunkSentCount++;

                    var percentRemaining = BytesRemaining / (float)_fileTransfer.FileSizeInBytes;

                    PercentComplete = 1 - percentRemaining;
                    CurrentBytesSent = fileChunkSize;

                    if (_fileTransfer.FileSizeInBytes > 10 * _bufferSize) continue;

                    EventLog.Add(new ServerEvent
                    {
                        EventType = ServerEventType.SentFileChunkToClient,
                        FileSizeInBytes = _fileTransfer.FileSizeInBytes,
                        CurrentFileBytesSent = fileChunkSize,
                        FileBytesRemaining = BytesRemaining,
                        FileChunkSentCount = FileChunkSentCount,
                        SocketSendCount = socketSendCount,
                        RequestId = RequestId
                    });

                    SocketEventOccurred?.Invoke(this, EventLog.Last());
                }

                Status = FileTransferStatus.AwaitingConfirmation;
                TransferCompleteTime = DateTime.Now;
                PercentComplete = 1;
                CurrentBytesSent = 0;
                BytesRemaining = 0;

                EventLog.Add(new ServerEvent
                {
                    EventType = ServerEventType.SendFileBytesComplete,
                    RemoteServerIpAddress = _fileTransfer.RemoteServerIpAddress,
                    RemoteServerPortNumber = _fileTransfer.RemoteServerPortNumber,
                    RequestId = RequestId
                });

                EventOccurred?.Invoke(this, EventLog.Last());

                return Result.Ok();
            }
        }

        public async Task<Result> ReceiveFileAsync(
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
                RemoteServerIpAddress = _fileTransfer.RemoteServerIpAddress,
                RemoteServerPortNumber = _fileTransfer.RemoteServerPortNumber,
                FileTransferStartTime = TransferStartTime,
                FileSizeInBytes = FileSizeInBytes,
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
                    RequestId = RequestId
                });

                EventOccurred?.Invoke(this, EventLog.Last());
            }

            // Read file bytes from transfer socket until 
            //      1. the entire file has been received OR 
            //      2. Data is no longer being received OR
            //      3, Transfer is canceled
            while (BytesRemaining > 0)
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
                var receivedBytes = new byte[CurrentBytesReceived];

                if (CurrentBytesReceived == 0)
                {
                    return Result.Fail("Socket is no longer receiving data, must abort file transfer");
                }

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
                        RequestId = RequestId
                    });

                    EventOccurred?.Invoke(this, EventLog.Last());
                }

                // this method fires on every socket read event, which could be hurdreds of thousands
                // of times depending on the file size and buffer size. Since this  event is only used
                // by myself when debugging small test files, I limited this event to only fire when 
                // the size of the file will result in less than 10 read events
                if (FileSizeInBytes < 10 * _bufferSize)
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
                    PercentComplete = PercentComplete
                });
            }

            if (InboundFileTransferStalled)
            {
                const string fileTransferStalledErrorMessage =
                    "Data is no longer bring received from remote client, file transfer has been canceled (ReceiveFileAsync)";

                Status = FileTransferStatus.Stalled;
                TransferCompleteTime = DateTime.Now;
                ErrorMessage = fileTransferStalledErrorMessage;

                return Result.Ok();
            }

            Status = FileTransferStatus.Complete;
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
                RequestId = RequestId
            });

            EventOccurred?.Invoke(this, EventLog.Last());

            return Result.Ok();
        }

        string GetFileName()
        {
            switch (TransferDirection)
            {
                case FileTransferDirection.Inbound:
                    return Path.GetFileName(_fileTransfer.RemoteFilePath);

                case FileTransferDirection.Outbound:
                    return Path.GetFileName(_fileTransfer.LocalFilePath);

                default:
                    return string.Empty;
            }
        }
    }
}
