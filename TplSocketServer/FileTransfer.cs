namespace TplSockets
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Net;

    using AaronLuna.Common.Extensions;
    using AaronLuna.Common.IO;
    using AaronLuna.Common.Result;
    
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
            ConfirmationMessage = string.Empty;
            ErrorMessage = string.Empty;

            RetryCounter = 1;
            RemoteServerTransferId = 0;
            TransferResponseCode = 0;
        }
        
        public int Id { get; set; }
        public int RemoteServerTransferId { get; set; }
        public FileTransferDirection TransferDirection { get; set; }
        public FileTransferInitiator Initiator { get; set; }
        public FileTransferStatus Status { get; set; }
        public long TransferResponseCode { get; set; }
        public int RetryCounter { get; set; }
        public bool RetryLimitExceeded { get; set; }
        public List<ServerEvent> EventLog { get; set; }
        public IPAddress MyLocalIpAddress { get; set; }
        public IPAddress MyPublicIpAddress { get; set; }
        public int MyServerPortNumber { get; set; }
        public IPAddress RemoteServerIpAddress { get; set; }
        public int RemoteServerPortNumber { get; set; }
        public string LocalFilePath { get; set; }
        public string LocalFolderPath { get; set; }
        public string RemoteFilePath { get; set; }
        public string RemoteFolderPath { get; set; }
        public long FileSizeInBytes { get; set; }
        public string FileSizeString => FileHelper.FileSizeToString(FileSizeInBytes);
        public DateTime RequestInitiatedTime { get; set; }
        public DateTime TransferStartTime { get; set; }
        public DateTime TransferCompleteTime { get; set; }
        public DateTime RetryLockoutExpireTime { get; set; }
        public TimeSpan TransferTimeSpan => TransferCompleteTime - TransferStartTime;
        public bool RetryLockoutExpired => RetryLockoutExpireTime > DateTime.Now;
        public string TransferTimeElapsed => TransferTimeSpan.ToFormattedString();
        public string TransferRate => GetTransferRate(TransferTimeSpan, FileSizeInBytes);
        public int CurrentBytesReceived { get; set; }
        public long TotalBytesReceived { get; set; }
        public int CurrentBytesSent { get; set; }
        public long BytesRemaining { get; set; }
        public int FileChunkSentCount { get; set; }
        public float PercentComplete { get; set; }
        public string ConfirmationMessage { get; set; }
        public string ErrorMessage { get; set; }

        public FileTransfer Duplicate(int newId)
        {
            var shallowCopy = (FileTransfer) this.MemberwiseClone();

            shallowCopy.Id = newId;
            shallowCopy.RemoteServerTransferId = 0;
            shallowCopy.RetryCounter = RetryCounter + 1;
            shallowCopy.Status = FileTransferStatus.AwaitingResponse;

            shallowCopy.EventLog = new List<ServerEvent>();
            shallowCopy.MyLocalIpAddress = new IPAddress(MyLocalIpAddress.GetAddressBytes());
            shallowCopy.MyPublicIpAddress = new IPAddress(MyPublicIpAddress.GetAddressBytes());
            shallowCopy.RemoteServerIpAddress = new IPAddress(RemoteServerIpAddress.GetAddressBytes());

            shallowCopy.LocalFilePath = string.Copy(LocalFilePath);
            shallowCopy.LocalFolderPath = string.Copy(LocalFolderPath);
            shallowCopy.RemoteFilePath = string.Copy(RemoteFilePath);
            shallowCopy.RemoteFolderPath = string.Copy(RemoteFolderPath);
            shallowCopy.ConfirmationMessage = string.Empty;
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
            var initiated = Initiator == FileTransferInitiator.Self
                ? $"Request sent: {RequestInitiatedTime:g}"
                : $"Request received: {RequestInitiatedTime:g}";

            var tasksRemaining = Status.TasksRemaining()
                ? $"Transfer complete: {TransferCompleteTime:g}"
                : $"Transfer started: {TransferStartTime:g}";

            var attempt = $"Attempt #{RetryCounter}";

            var lockout = RetryLimitExceeded
                ? $"Lockout expires: {RetryLockoutExpireTime:g}"
                : attempt;

            var summaryNotStarted =
                $"{TransferDirection} File Transfer from {RemoteServerIpAddress}:{RemoteServerPortNumber}" +
                $"{Environment.NewLine}{initiated}\t{lockout}{Environment.NewLine}";

            var summaryStarted =
                $"[{Status}] {lockout}" +
                $"{Environment.NewLine}{TransferDirection} File Transfer from {RemoteServerIpAddress}:{RemoteServerPortNumber}" +
                Environment.NewLine + tasksRemaining + Environment.NewLine;

            return Status == FileTransferStatus.AwaitingResponse
                ? summaryNotStarted
                : summaryStarted;
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

    public static class FileTransferListExtensions
    {
        public static Result<FileTransfer> GetFileTransferById(this List<FileTransfer> fileTransferList, int id)
        {
            var matches = fileTransferList.Select(t => t).Where(t => t.Id == id).ToList();

            if (matches.Count == 0)
            {
                return Result.Fail<FileTransfer>($"No file transfer was found with an ID value of {id}");
            }

            if (matches.Count > 1)
            {
                return Result.Fail<FileTransfer>($"Found {matches.Count} file transfers with the same ID value of {id}");
            }

            return Result.Ok(matches[0]);
        }

        public static Result<FileTransfer> GetFileTransferByResponseCode(this List<FileTransfer> fileTransferList, long responseCode)
        {
            var matches = fileTransferList.Select(t => t).Where(t => t.TransferResponseCode == responseCode).ToList();

            if (matches.Count == 0)
            {
                return Result.Fail<FileTransfer>($"No file transfer was found with a response code value of {responseCode}");
            }

            if (matches.Count > 1)
            {
                return Result.Fail<FileTransfer>($"Found {matches.Count} file transfers with the same response code value of {responseCode}");
            }

            return Result.Ok(matches[0]);
        }
    }

    public class FileInfoList : List<(string filePath, long fileSizeBytes)>
    {
        public FileInfoList() { }

        public FileInfoList(string folderPath)
        {
            if (!Directory.Exists(folderPath)) return;

            var fileList =
                Directory.GetFiles(folderPath).ToList()
                    .Select(f => new FileInfo(f)).Where(fi => !fi.Name.StartsWith('.'))
                    .Select(fi => fi.ToString()).ToList();
            
            foreach (var file in fileList)
            {
                var fileSize = new FileInfo(file).Length;
                (string filePath, long fileSizeBytes) fileInfo = (filePath: file, fileSizeBytes: fileSize);
                Add(fileInfo);
            }
        }
    }

    public enum FileTransferDirection
    {
        None,
        Inbound,
        Outbound
    }

    public enum FileTransferInitiator
    {
        None,
        Self,
        RemoteServer
    }

    public enum FileTransferStatus
    {
        None,
        AwaitingResponse,
        Accepted,
        Rejected,
        InProgress,
        Stalled,
        Cancelled,
        AwaitingConfirmation,
        Complete,
        RetryLimitExceeded,
        Error
    }

    public static class FileTransferInitiatorExtensions
    {
        public static string ToString(this FileTransferInitiator initiator)
        {
            switch (initiator)
            {
                case FileTransferInitiator.RemoteServer:
                    return "Remote Server";

                case FileTransferInitiator.Self:
                    return initiator.ToString();

                default:
                    return "N/A";
            }
        }
    }

    public static class FileTransferStatusExtensions
    {
        public static string ToString(this FileTransferStatus status)
        {
            switch (status)
            {
                case FileTransferStatus.AwaitingResponse:
                    return "Awaiting Response";

                case FileTransferStatus.InProgress:
                    return "In Progress";

                case FileTransferStatus.AwaitingConfirmation:
                    return "Awaiting Confirmation";

                case FileTransferStatus.RetryLimitExceeded:
                    return "Error: Retry Limit Exceeded";

                case FileTransferStatus.Accepted:
                case FileTransferStatus.Rejected:
                case FileTransferStatus.Stalled:
                case FileTransferStatus.Cancelled:
                case FileTransferStatus.Complete:
                case FileTransferStatus.Error:
                    return status.ToString();

                default:
                    return "N/A";
            }
        }

        public static bool TasksRemaining(this FileTransferStatus status)
        {
            switch (status)
            {
                case FileTransferStatus.AwaitingResponse:
                case FileTransferStatus.Accepted:
                case FileTransferStatus.InProgress:
                case FileTransferStatus.AwaitingConfirmation:
                    return true;

                default:
                    return false;
            }
        }
    }
}
