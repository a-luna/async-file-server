namespace TplSocketServer
{
    using System;
    using System.Globalization;
    using System.IO;
    using System.Text;

    internal class MessageWrapper
    {
        public const int SizeOfInt32InBytes = 4;
        public const int SizeOfCharInBytes = 2;

        public MessageWrapper()
        {
            FileName = string.Empty;
            FileSizeInBytes = 0;
            LocalFolderPath = string.Empty;
            RemoteFolderPath = string.Empty;
            LocalFilePath = string.Empty;
            RemoteFilePath = string.Empty;
            ClientIpAddress = string.Empty;
            ClientPortNumber = 0;
        }

        public string FileName { get; set; }
        public long FileSizeInBytes { get; set; }
        public string LocalFolderPath { get; set; }
        public string RemoteFolderPath { get; set; }
        public string LocalFilePath { get; set; }
        public string RemoteFilePath { get; set; }
        public string ClientIpAddress { get; set; }
        public int ClientPortNumber { get; set; }

        public byte[] ConstuctTextMessageRequest(string message, string localIpAddress, int localPort)
        {
            byte[] requestFlag = BitConverter.GetBytes((int)TransferType.TextMessage);
            byte[] messageData = Encoding.UTF8.GetBytes(message);
            byte[] messageDataLen = BitConverter.GetBytes(messageData.Length);
            byte[] thisServerIpData = Encoding.UTF8.GetBytes(localIpAddress);
            byte[] thisServerIpLen = BitConverter.GetBytes(thisServerIpData.Length);
            byte[] thisServerPortData = Encoding.UTF8.GetBytes(localPort.ToString(CultureInfo.InvariantCulture));
            byte[] thisServerPortLen = BitConverter.GetBytes(thisServerPortData.Length);

            var messageWrapper = new byte[requestFlag.Length + messageDataLen.Length + messageData.Length +
                                          thisServerIpLen.Length + thisServerIpData.Length + thisServerPortLen.Length +
                                          thisServerPortData.Length];

            requestFlag.CopyTo(messageWrapper, 0);

            messageDataLen.CopyTo(messageWrapper, SizeOfInt32InBytes);
            messageData.CopyTo(messageWrapper, SizeOfInt32InBytes + messageDataLen.Length);

            thisServerIpLen.CopyTo(messageWrapper, SizeOfInt32InBytes + messageDataLen.Length + messageData.Length);
            thisServerIpData.CopyTo(messageWrapper, SizeOfInt32InBytes + messageDataLen.Length + messageData.Length + thisServerIpLen.Length);

            thisServerPortLen.CopyTo(messageWrapper, SizeOfInt32InBytes + messageDataLen.Length + messageData.Length + thisServerIpLen.Length + thisServerIpData.Length);
            thisServerPortData.CopyTo(messageWrapper, SizeOfInt32InBytes + messageDataLen.Length + messageData.Length + thisServerIpLen.Length + thisServerIpData.Length + thisServerPortLen.Length);

            return messageWrapper;
        }

        public byte[] ConstructInboundFileTransferRequest(string remoteFilePath, string localIpAddress, int localPort, string localFolderPath)
        {
            RemoteFilePath = remoteFilePath;
            RemoteFolderPath = Path.GetDirectoryName(remoteFilePath);
            FileName = Path.GetFileName(remoteFilePath) ?? string.Empty;
            LocalFolderPath = localFolderPath;
            LocalFilePath = Path.Combine(localFolderPath, FileName);

            byte[] requestFlag = BitConverter.GetBytes((int)TransferType.OutboundFileTransfer);
            byte[] remoteFilePathData = Encoding.UTF8.GetBytes(RemoteFilePath);
            byte[] remoteFilePathLen = BitConverter.GetBytes(remoteFilePathData.Length);
            byte[] thisServerIpData = Encoding.UTF8.GetBytes(localIpAddress);
            byte[] thisServerIpLen = BitConverter.GetBytes(thisServerIpData.Length);
            byte[] thisServerPortData = Encoding.UTF8.GetBytes(localPort.ToString(CultureInfo.InvariantCulture));
            byte[] thisServerPortLen = BitConverter.GetBytes(thisServerPortData.Length);
            byte[] targetDirData = Encoding.UTF8.GetBytes(LocalFolderPath);
            byte[] targetDirLen = BitConverter.GetBytes(targetDirData.Length);

            var messageHeaderData = new byte[requestFlag.Length + remoteFilePathLen.Length + remoteFilePathData.Length
                                             + thisServerIpLen.Length + thisServerIpData.Length
                                             + thisServerPortLen.Length + thisServerPortData.Length
                                             + targetDirLen.Length + targetDirData.Length];

            requestFlag.CopyTo(messageHeaderData, 0);

            remoteFilePathLen.CopyTo(messageHeaderData, SizeOfInt32InBytes);
            remoteFilePathData.CopyTo(messageHeaderData, SizeOfInt32InBytes + remoteFilePathLen.Length);

            thisServerIpLen.CopyTo(messageHeaderData, SizeOfInt32InBytes + remoteFilePathLen.Length + remoteFilePathData.Length);
            thisServerIpData.CopyTo(messageHeaderData, SizeOfInt32InBytes + remoteFilePathLen.Length + remoteFilePathData.Length + thisServerIpLen.Length);

            thisServerPortLen.CopyTo(messageHeaderData, SizeOfInt32InBytes + remoteFilePathLen.Length + remoteFilePathData.Length + thisServerIpLen.Length + thisServerIpData.Length);
            thisServerPortData.CopyTo(messageHeaderData, SizeOfInt32InBytes + remoteFilePathLen.Length + remoteFilePathData.Length + thisServerIpLen.Length + thisServerIpData.Length + thisServerPortLen.Length);

            targetDirLen.CopyTo(messageHeaderData, SizeOfInt32InBytes + remoteFilePathLen.Length + remoteFilePathData.Length + thisServerIpLen.Length + thisServerIpData.Length + thisServerPortLen.Length + thisServerPortData.Length);
            targetDirData.CopyTo(messageHeaderData, SizeOfInt32InBytes + remoteFilePathLen.Length + remoteFilePathData.Length + thisServerIpLen.Length + thisServerIpData.Length + thisServerPortLen.Length + thisServerPortData.Length + targetDirLen.Length);

            return messageHeaderData;
        }

        public byte[] ConstructOutboundFileTransferRequest(string localFilePath, long fileSizeBytes, string remoteFolderPath)
        {
            LocalFilePath = localFilePath;
            LocalFolderPath = Path.GetDirectoryName(localFilePath);
            FileName = Path.GetFileName(localFilePath);
            FileSizeInBytes = fileSizeBytes;
            RemoteFilePath = remoteFolderPath;
            RemoteFolderPath = Path.GetDirectoryName(remoteFolderPath) ?? string.Empty;

            byte[] requestFlag = BitConverter.GetBytes((int)TransferType.InboundFileTransfer);

            string fileName = Path.GetFileName(LocalFilePath);
            if (fileName == null)
            {
                return null;
            }

            byte[] fileNameData = Encoding.UTF8.GetBytes(fileName);
            byte[] fileNameLen = BitConverter.GetBytes(fileNameData.Length);

            byte[] sizeBytesData =
                Encoding.UTF8.GetBytes(FileSizeInBytes.ToString(CultureInfo.InvariantCulture));

            byte[] sizeBytesLen = BitConverter.GetBytes(sizeBytesData.Length);

            byte[] targetDirData = Encoding.UTF8.GetBytes(RemoteFolderPath);
            byte[] targetDirLen = BitConverter.GetBytes(targetDirData.Length);

            var messageHeaderData = new byte[requestFlag.Length + fileNameLen.Length + fileNameData.Length
                                             + sizeBytesLen.Length + sizeBytesData.Length + targetDirLen.Length
                                             + targetDirData.Length];

            requestFlag.CopyTo(messageHeaderData, 0);

            fileNameLen.CopyTo(messageHeaderData, SizeOfInt32InBytes);
            fileNameData.CopyTo(messageHeaderData, SizeOfInt32InBytes + fileNameLen.Length);

            sizeBytesLen.CopyTo(messageHeaderData, SizeOfInt32InBytes + fileNameLen.Length + fileNameData.Length);
            sizeBytesData.CopyTo(messageHeaderData, SizeOfInt32InBytes + fileNameLen.Length + fileNameData.Length + sizeBytesLen.Length);

            targetDirLen.CopyTo(messageHeaderData, SizeOfInt32InBytes + fileNameLen.Length + fileNameData.Length + sizeBytesLen.Length + sizeBytesData.Length);
            targetDirData.CopyTo(messageHeaderData, SizeOfInt32InBytes + fileNameLen.Length + fileNameData.Length + sizeBytesLen.Length + sizeBytesData.Length + targetDirLen.Length);

            return messageHeaderData;
        }
    }
}
