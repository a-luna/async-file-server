using System;
using System.IO;
using AaronLuna.Common.Extensions;
using AaronLuna.Common.IO;

namespace AaronLuna.AsyncSocketServer.FileTransfers
{
    public class FileTransfer
    {
        private bool _idHasBeenSet;

        public FileTransfer()
        {
            RemoteServerInfo = new ServerInfo();
            TransferDirection = TransferDirection.None;
            Initiator = FileTransferInitiator.None;
            Status = FileTransferStatus.None;
            FileName = string.Empty;
            LocalFolderPath = string.Empty;
            RemoteFolderPath = string.Empty;
            ErrorMessage = string.Empty;
            RetryCounter = 1;
            RequestInitiatedTime = DateTime.MinValue;
            TransferStartTime = DateTime.MinValue;
            TransferCompleteTime = DateTime.MinValue;
            RetryLockoutExpireTime = DateTime.MinValue;
        }

        public int Id { get; private set; }
        public int RemoteServerTransferId { get; set; }
        public long TransferResponseCode { get; set; }
        public TransferDirection TransferDirection { get; set; }
        public FileTransferInitiator Initiator { get; set; }
        public FileTransferStatus Status { get; set; }
        public string FileName { get; set; }
        public long FileSizeInBytes { get; set; }
        public string LocalFolderPath { get; set; }
        public string RemoteFolderPath { get; set; }
        public DateTime RequestInitiatedTime { get; set; }
        public DateTime TransferStartTime { get; set; }
        public DateTime TransferCompleteTime { get; set; }
        public ServerInfo RemoteServerInfo { get; set; }
        public string ErrorMessage { get; set; }
        public int CurrentBytesReceived { get; set; }
        public long TotalBytesReceived { get; set; }
        public int CurrentBytesSent { get; set; }
        public long BytesRemaining { get; set; }
        public int FileChunkSentCount { get; set; }
        public float PercentComplete { get; set; }
        public bool OutboundFileTransferStalled { get; set; }
        public bool InboundFileTransferStalled { get; set; }
        public int RetryCounter { get; set; }
        public int RemoteServerRetryLimit { get; set; }
        public DateTime RetryLockoutExpireTime { get; set; }

        public string LocalFilePath => Path.Combine(LocalFolderPath, FileName);
        public string RemoteFilePath => Path.Combine(RemoteFolderPath, FileName);
        public string FileSizeString => FileHelper.FileSizeToString(FileSizeInBytes);
        public TimeSpan TransferTimeSpan => TransferCompleteTime - TransferStartTime;
        public string TransferTimeElapsed => TransferTimeSpan.ToFormattedString();
        public string TransferRate => GetTransferRate(TransferTimeSpan, FileSizeInBytes);
        public string RetryLockoutTimeRemianing => (RetryLockoutExpireTime - DateTime.Now).ToFormattedString();
        public bool RetryLockoutExpired => RetryLockoutExpireTime < DateTime.Now;
        public bool AwaitingResponse => Status == FileTransferStatus.Pending;
        public bool TransferStalled => Status == FileTransferStatus.Stalled;
        public bool TransferNeverStarted => Status.TransferNeverStarted();
        public bool TransferStartedButDidNotComplete => Status.TransferStartedButDidNotComplete();
        public bool TransfercompletedSucessfully => Status.TransfercompletedSucessfully();

        public void SetId(int id)
        {
            if (_idHasBeenSet) return;

            Id = id;
            _idHasBeenSet = true;
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

        public FileTransfer Duplicate()
        {
            var shallowCopy = (FileTransfer) MemberwiseClone();

            shallowCopy.TransferDirection = TransferDirection;
            shallowCopy.Initiator = Initiator;
            shallowCopy.Status = Status;
            shallowCopy.FileName = string.Copy(FileName);
            shallowCopy.LocalFolderPath = string.Copy(LocalFolderPath);
            shallowCopy.RemoteFolderPath = string.Copy(RemoteFolderPath);
            shallowCopy.RequestInitiatedTime = new DateTime(RequestInitiatedTime.Ticks);
            shallowCopy.TransferStartTime = new DateTime(TransferStartTime.Ticks);
            shallowCopy.TransferCompleteTime = new DateTime(TransferCompleteTime.Ticks);
            shallowCopy.RemoteServerInfo = RemoteServerInfo.Duplicate();
            shallowCopy.RetryLockoutExpireTime = new DateTime(RetryLockoutExpireTime.Ticks);
            shallowCopy.ErrorMessage = string.Copy(ErrorMessage);

            return shallowCopy;
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
            var fileName = $"   File Name..........: {FileName}";
            var fileSize = $"   File Size..........: {FileSizeString}";
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

            details += $" File Name..............: {FileName}{Environment.NewLine}";
            details += $" File Size..............: {FileSizeString} ({FileSizeInBytes:N0} bytes){Environment.NewLine}";
            details += $" Local Folder...........: {LocalFolderPath}{Environment.NewLine}";

            var remoteFolder = $" Remote Folder..........: {RemoteFolderPath}{Environment.NewLine}{Environment.NewLine}";

            details += string.IsNullOrEmpty(RemoteFolderPath)
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

            details += TransferDirection == TransferDirection.Inbound
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
