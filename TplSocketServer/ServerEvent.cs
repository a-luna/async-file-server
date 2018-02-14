namespace TplSocketServer
{
    using System;

    public delegate void ServerEventDelegate(ServerEvent serverEvent);
    public class ServerEvent
    {
        public ServerEventType EventType { get; set; } = ServerEventType.None;

        public TransferType TransferType { get; set; }
        public string TextMessage { get; set; }
        public string RemoteServerIpAddress { get; set; }
        public int RemoteServerPortNumber { get; set; }
        public string LocalFolder { get; set; }
        public string RemoteFolder { get; set; }
        public string FileName { get; set; }
        public long FileSizeInBytes { get; set; }
        public string FileSizeString => FileSizeInBytes.ConvertBytesForDisplay();
        public DateTime FileTransferStartTime { get; set; }
        public DateTime FileTransferCompleteTime { get; set; }
        public TimeSpan FileTransferElapsedTime => FileTransferCompleteTime - FileTransferStartTime;
        public string FileTransferElapsedTimeString => FileTransferElapsedTime.ToFormattedString();
        public string FileTransferRate => CalculateTransferRate(FileTransferElapsedTime, FileSizeInBytes);
        public int CurrentBytesReceivedFromSocket { get; set; }
        public long TotalBytesReceivedFromSocket { get; set; }
        public long BytesRemainingInFile { get; set; }
        public int ReceiveBytesCount { get;set; }
        public float PercentComplete { get; set; }
        public string ConfirmationMessage { get; set; }
        public string ErrorMessage { get; set; }

        private static string CalculateTransferRate(TimeSpan elapsed, long bytesReceived)
        {
            if (elapsed == TimeSpan.MinValue || bytesReceived == 0)
            {
                return string.Empty;
            }

            var totalMilliseconds = elapsed.Ticks / 10_000;
            var bytesPerMs = bytesReceived / (double)totalMilliseconds;
            var kilobitsPerSecond = (bytesPerMs * 1000) / 1024;

            return $"{kilobitsPerSecond:F1} kb/s";
        }
    }
}
