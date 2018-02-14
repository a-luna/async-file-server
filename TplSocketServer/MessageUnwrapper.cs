namespace TplSocketServer
{
    using System;
    using System.IO;
    using System.Text;

    internal class MessageUnwrapper
    {
        public const int SizeOfInt32InBytes = 4;
        public const int SizeOfCharInBytes = 2;

        public MessageUnwrapper()
        {
            Message = string.Empty;
            FileName = string.Empty;
            FileSizeInBytes = 0;
            LocalFolderPath = string.Empty;
            RemoteFolderPath = string.Empty;
            LocalFilePath = string.Empty;
            RemoteFilePath = string.Empty;
            ClientIpAddress = string.Empty;
            ClientPortNumber = 0;
        }

        public string Message { get; set; }
        public string FileName { get; set; }
        public long FileSizeInBytes { get; set; }
        public string LocalFolderPath { get; set; }
        public string RemoteFolderPath { get; set; }
        public string LocalFilePath { get; set; }
        public string RemoteFilePath { get; set; }
        public string ClientIpAddress { get; set; }
        public int ClientPortNumber { get; set; }

        public static int DetermineTransferType(byte[] buffer)
        {
            return BitConverter.ToInt32(buffer, 0);
        }

        public void ReadTextMessageRequest(byte[] buffer)
        {
            int messageDataLen = BitConverter.ToInt32(buffer, SizeOfInt32InBytes);
            Message = Encoding.UTF8.GetString(buffer, SizeOfInt32InBytes * 2, messageDataLen);

            int clientIpLen = BitConverter.ToInt32(buffer, (SizeOfInt32InBytes * 2) + messageDataLen);
            ClientIpAddress = Encoding.UTF8.GetString(buffer, (SizeOfInt32InBytes * 3) + messageDataLen, clientIpLen);

            int clientPortLen = BitConverter.ToInt32(buffer, (SizeOfInt32InBytes * 3) + messageDataLen + clientIpLen);
            ClientPortNumber = int.Parse(Encoding.UTF8.GetString(buffer, (SizeOfInt32InBytes * 4) + messageDataLen + clientIpLen, clientPortLen));
        }
        public void ReadInboundFileTransferRequest(byte[] buffer)
        {
            int fileNameLen = BitConverter.ToInt32(buffer, SizeOfInt32InBytes);
            FileName = Encoding.UTF8.GetString(buffer, SizeOfInt32InBytes * 2, fileNameLen);

            int fileSizeLen = BitConverter.ToInt32(buffer, (SizeOfInt32InBytes * 2) + fileNameLen);
            FileSizeInBytes = Convert.ToInt64(
                Encoding.UTF8.GetString(buffer, (SizeOfInt32InBytes * 3) + fileNameLen, fileSizeLen));

            int targetDirLen = BitConverter.ToInt32(buffer, (SizeOfInt32InBytes * 3) + fileNameLen + fileSizeLen);
            LocalFolderPath = Encoding.UTF8.GetString(
                buffer,
                (SizeOfInt32InBytes * 4) + fileNameLen + fileSizeLen,
                targetDirLen);

            LocalFilePath = Path.Combine(LocalFolderPath, FileName);
        }

        public void ReadOutboundFileTransferRequest(byte[] buffer)
        {
            int requestedFilePathLen = BitConverter.ToInt32(buffer, SizeOfInt32InBytes);
            LocalFilePath = Encoding.UTF8.GetString(buffer, SizeOfInt32InBytes * 2, requestedFilePathLen);

            LocalFolderPath = Path.GetDirectoryName(LocalFilePath);
            FileName = Path.GetFileName(LocalFilePath);

            int clientIpLen = BitConverter.ToInt32(buffer, (SizeOfInt32InBytes * 2) + requestedFilePathLen);

            ClientIpAddress = Encoding.UTF8.GetString(
                buffer,
                (SizeOfInt32InBytes * 3) + requestedFilePathLen,
                clientIpLen);

            int clientPortLen = BitConverter.ToInt32(
                buffer,
                (SizeOfInt32InBytes * 3) + requestedFilePathLen + clientIpLen);

            ClientPortNumber = int.Parse(
                Encoding.UTF8.GetString(
                    buffer,
                    (SizeOfInt32InBytes * 4) + requestedFilePathLen + clientIpLen,
                    clientPortLen));

            int clientFolderLen = BitConverter.ToInt32(
                buffer,
                (SizeOfInt32InBytes * 4) + requestedFilePathLen + clientIpLen + clientPortLen);

            RemoteFolderPath = Encoding.UTF8.GetString(
                buffer,
                (SizeOfInt32InBytes * 5) + requestedFilePathLen + clientIpLen + clientPortLen,
                clientFolderLen);

            RemoteFilePath = Path.Combine(RemoteFolderPath, FileName);
        }
    }
}
