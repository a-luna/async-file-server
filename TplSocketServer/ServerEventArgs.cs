namespace TplSocketServer
{
    using System;
    using System.Collections.Generic;

    using AaronLuna.Common.Extensions;
    using AaronLuna.Common.IO;

    public class ServerEventArgs : EventArgs
    {
        public ServerEventType EventType { get; set; } = ServerEventType.None;

        public int MessageLength { get; set; }
        public int UnreadByteCount { get; set; }
        public RequestType RequestType { get; set; }
        public string TextMessage { get; set; }
        public string RemoteServerIpAddress { get; set; }
        public int RemoteServerPortNumber { get; set; }
        public string LocalIpAddress { get; set; }
        public int LocalPortNumber { get; set; }
        public string PublicIpAddress { get; set; }
        public string LocalFolder { get; set; }
        public string RemoteFolder { get; set; }
        public string FileName { get; set; }
        public long FileSizeInBytes { get; set; }
        public string FileSizeString => FileHelper.FileSizeToString(FileSizeInBytes);
        public List<(string, long)> FileInfoList { get; set; }
        public DateTime FileTransferStartTime { get; set; }
        public DateTime FileTransferCompleteTime { get; set; }
        public TimeSpan FileTransferElapsedTime => FileTransferCompleteTime - FileTransferStartTime;
        public string FileTransferElapsedTimeString => FileTransferElapsedTime.ToFormattedString();
        public string FileTransferRate => FileHelper.GetTransferRate(FileTransferElapsedTime, FileSizeInBytes);
        public int CurrentBytesReceivedFromSocket { get; set; }
        public long TotalBytesReceivedFromSocket { get; set; }
        public long BytesRemainingInFile { get; set; }
        public int ReceiveBytesCount { get;set; }
        public float PercentComplete { get; set; }
        public string ConfirmationMessage { get; set; }
        public string ErrorMessage { get; set; }
    }
}
