namespace TplSockets
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;

    using AaronLuna.Common.Extensions;
    using AaronLuna.Common.IO;

    public class FileTransfer
    {
        public FileTransfer()
        {
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
        
            var transferStatus = $"[{Status}] {attempt}";
            var fileName = $"File Name: {FileName}";
            var fileSize = $"   File Size: {FileSizeString}";
            var remoteServerInfo = $"   {TransferDirection} File Transfer from {RemoteServerIpAddress}:{RemoteServerPortNumber}";

            var requestTime = Initiator == FileTransferInitiator.Self
                ? $"   Request sent:      {RequestInitiatedTime:MM/dd/yyyy hh:mm:ss.fff tt}"
                : $"   Request received:  {RequestInitiatedTime:MM/dd/yyyy hh:mm:ss.fff tt}";

            var transferTime = Status.TasksRemaining()
                ? $"   Transfer started:  {TransferStartTime:MM/dd/yyyy hh:mm:ss.fff tt}"
                : $"   Transfer complete: {TransferCompleteTime:MM/dd/yyyy hh:mm:ss.fff tt}";
                
            var lockoutExpireTime = Status == FileTransferStatus.RetryLimitExceeded
                ? $"   Lockout expires:   {RetryLockoutExpireTime:MM/dd/yyyy hh:mm:ss.fff tt}"
                : transferTime;

            var summaryNotStarted = fileName + Environment.NewLine + fileSize + Environment.NewLine + remoteServerInfo + Environment.NewLine + requestTime + Environment.NewLine + lockoutExpireTime + Environment.NewLine;
            var summaryStarted = transferStatus + Environment.NewLine + "   " + summaryNotStarted;

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
            retryLockout += $"Retry Limit Exceeded:\t{transferLimitExceeded}{Environment.NewLine}";
            retryLockout += $"Retry Lockout Expires:\t{lockoutExpireTime}{Environment.NewLine}{Environment.NewLine}";

            var details = string.Empty;
            details += $"ID:\t\t\t{Id}{Environment.NewLine}";
            details += $"Remote Server ID:\t{RemoteServerTransferId}{Environment.NewLine}";
            details += $"Response Code:\t\t{TransferResponseCode}{Environment.NewLine}";
            details += $"Initiator:\t\t{Initiator}{Environment.NewLine}";
            details += $"Direction:\t\t{TransferDirection}{Environment.NewLine}";
            details += $"Status:\t\t\t{Status}{Environment.NewLine}{Environment.NewLine}";
            
            details += $"File Name:\t\t{FileName}{Environment.NewLine}";
            details += $"File Size:\t\t{FileSizeString} ({FileSizeInBytes:N0} bytes){Environment.NewLine}";
            details += $"Local Folder:\t\t{LocalFolderPath}{Environment.NewLine}";
            details += $"Remote Folder:\t\t{RemoteFolderPath}{Environment.NewLine}{Environment.NewLine}";
            
            details += $"Request Initiated:\t{requestInitiated}{Environment.NewLine}";
            details += $"Transfer Started:\t{transferStarted}{Environment.NewLine}";
            details += $"Transfer Completed:\t{transferComplete}{Environment.NewLine}";
            details += $"Transfer Time Elapsed:\t{timeElapsed}{Environment.NewLine}";
            details += $"Transfer Rate:\t\t{TransferRate}{Environment.NewLine}{Environment.NewLine}";

            details += $"Percent Complete:\t{PercentComplete:P2}{Environment.NewLine}";

            var bytesReceived = string.Empty;
            bytesReceived += $"Total Bytes Received:\t{TotalBytesReceived}{Environment.NewLine}";
            bytesReceived += $"Current Bytes Received:\t{CurrentBytesReceived}{Environment.NewLine}";
            bytesReceived += $"Bytes Remaining:\t{BytesRemaining}{Environment.NewLine}{Environment.NewLine}";

            var bytesSent = string.Empty;
            bytesSent += $"File Chunks Sent:\t{FileChunkSentCount}{Environment.NewLine}";
            bytesSent += $"Current Bytes Sent:\t{CurrentBytesSent}{Environment.NewLine}";
            bytesSent += $"Bytes Remaining:\t{BytesRemaining}{Environment.NewLine}{Environment.NewLine}";

            details += TransferDirection == FileTransferDirection.Inbound
                ? bytesReceived
                : bytesSent;

            details += $"Transfer Attempt #:\t{RetryCounter}{Environment.NewLine}";

            details += string.IsNullOrEmpty(ErrorMessage)
                ? string.Empty
                : $"Error Message:\t{ErrorMessage}{Environment.NewLine}";

            details += $"Max Download Attempts:\t{RemoteServerRetryLimit}{Environment.NewLine}";

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
