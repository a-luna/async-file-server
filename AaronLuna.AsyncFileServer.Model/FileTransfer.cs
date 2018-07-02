namespace AaronLuna.AsyncFileServer.Model
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Sockets;

    using Common.Extensions;
    using Common.IO;

    public class FileTransfer
    {
        public FileTransfer(int bufferSize)
        {
            Buffer = new byte[bufferSize];
            TransferDirection = FileTransferDirection.None;
            Initiator = FileTransferInitiator.None;
            Status = FileTransferStatus.None;

            EventLog = new List<ServerEvent>();
            MyLocalIpAddress = IPAddress.None;
            MyPublicIpAddress = IPAddress.None;
            RemoteServerIpAddress = IPAddress.None;

            RequestInitiatedTime = DateTime.MinValue;
            TransferStartTime = DateTime.MinValue;
            TransferCompleteTime = DateTime.MinValue;
            RetryLockoutExpireTime = DateTime.MinValue;

            LocalFilePath = string.Empty;
            LocalFolderPath = string.Empty;
            RemoteFilePath = string.Empty;
            RemoteFolderPath = string.Empty;
            ErrorMessage = string.Empty;

            RetryCounter = 1;
            RemoteServerTransferId = 0;
            TransferResponseCode = 0;
        }

        public byte[] Buffer { get; set; }
        public Socket ReceiveSocket { get; set; }
        public Socket SendSocket { get; set; }

        public int Id { get; set; }
        public int RemoteServerTransferId { get; set; }
        public long TransferResponseCode { get; set; }
        public List<ServerEvent> EventLog { get; set; }

        public FileTransferDirection TransferDirection { get; set; }
        public FileTransferInitiator Initiator { get; set; }
        public FileTransferStatus Status { get; set; }

        public IPAddress MyLocalIpAddress { get; set; }
        public IPAddress MyPublicIpAddress { get; set; }
        public int MyServerPortNumber { get; set; }
        public IPAddress RemoteServerIpAddress { get; set; }
        public int RemoteServerPortNumber { get; set; }

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

        public string LocalFilePath { get; set; }
        public string LocalFolderPath { get; set; }
        public string RemoteFilePath { get; set; }
        public string RemoteFolderPath { get; set; }
        public string FileName => Path.GetFileName(LocalFilePath);

        public long FileSizeInBytes { get; set; }
        public string FileSizeString => FileHelper.FileSizeToString(FileSizeInBytes);
        public int CurrentBytesReceived { get; set; }
        public long TotalBytesReceived { get; set; }
        public int CurrentBytesSent { get; set; }
        public long BytesRemaining { get; set; }
        public int FileChunkSentCount { get; set; }
        public float PercentComplete { get; set; }
        public string TransferRate => GetTransferRate(TransferTimeSpan, FileSizeInBytes);

        public string ErrorMessage { get; set; }

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

        public FileTransfer Duplicate(int newId)
        {
            var shallowCopy = (FileTransfer) MemberwiseClone();

            shallowCopy.Id = newId;
            shallowCopy.RemoteServerTransferId = 0;
            shallowCopy.TransferResponseCode = 0;
            shallowCopy.Status = FileTransferStatus.AwaitingResponse;
            shallowCopy.RetryCounter = 0;

            shallowCopy.EventLog = new List<ServerEvent>();
            shallowCopy.MyLocalIpAddress = new IPAddress(MyLocalIpAddress.GetAddressBytes());
            shallowCopy.MyPublicIpAddress = new IPAddress(MyPublicIpAddress.GetAddressBytes());
            shallowCopy.RemoteServerIpAddress = new IPAddress(RemoteServerIpAddress.GetAddressBytes());

            shallowCopy.LocalFilePath = string.Copy(LocalFilePath);
            shallowCopy.LocalFolderPath = string.Copy(LocalFolderPath);
            shallowCopy.RemoteFilePath = string.Copy(RemoteFilePath);
            shallowCopy.RemoteFolderPath = string.Copy(RemoteFolderPath);
            shallowCopy.ErrorMessage = string.Empty;

            shallowCopy.RequestInitiatedTime = DateTime.MinValue;
            shallowCopy.TransferStartTime = DateTime.MinValue;
            shallowCopy.TransferCompleteTime = DateTime.MinValue;
            shallowCopy.RetryLockoutExpireTime = DateTime.MinValue;

            shallowCopy.CurrentBytesReceived = 0;
            shallowCopy.TotalBytesReceived = 0;
            shallowCopy.CurrentBytesSent = 0;
            shallowCopy.BytesRemaining = 0;
            shallowCopy.FileChunkSentCount = 0;
            shallowCopy.PercentComplete = 0;

            return shallowCopy;
        }

        public override string ToString()
        {
            var retryLimit = RemoteServerRetryLimit == 0
                ? string.Empty
                : $"/{RemoteServerRetryLimit}{Environment.NewLine}";

            var attempt = $"Attempt #{RetryCounter}{retryLimit}";

            var transferStatus = $"[{Status}] {attempt}{Environment.NewLine}";
            var requestType = $"{TransferDirection} file transfer initiated by {Initiator}{Environment.NewLine}{Environment.NewLine}";
            var fileName = $"   File Name........: {FileName}{Environment.NewLine}";
            var fileSize = $"   File Size........: {FileSizeString}{Environment.NewLine}";
            var remoteServerInfo = $"   Remote Server....: {RemoteServerIpAddress}:{RemoteServerPortNumber}{Environment.NewLine}";

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

            details += $" File Name..............: {FileName}{Environment.NewLine}";
            details += $" File Size..............: {FileSizeString} ({FileSizeInBytes:N0} bytes){Environment.NewLine}";
            details += $" Local Folder...........: {LocalFolderPath}{Environment.NewLine}";

            var remoteFolder = $" Remote Folder..........: {RemoteFolderPath}{Environment.NewLine}{Environment.NewLine}";

            details += string.IsNullOrEmpty(RemoteFolderPath)
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
    }
}
