namespace TplSocketServer
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Text;

    internal static class MessageWrapper
    {
        public const int SizeOfInt32InBytes = 4;
        public const int SizeOfCharInBytes = 2;

        public static byte[] ConstuctTextMessageRequest(string message, string localIpAddress, int localPort)
        {
            var requestFlag = BitConverter.GetBytes((int)TransferType.TextMessage);
            var messageData = Encoding.UTF8.GetBytes(message);
            var messageDataLen = BitConverter.GetBytes(messageData.Length);
            var thisServerIpData = Encoding.UTF8.GetBytes(localIpAddress);
            var thisServerIpLen = BitConverter.GetBytes(thisServerIpData.Length);
            var thisServerPortData = Encoding.UTF8.GetBytes(localPort.ToString(CultureInfo.InvariantCulture));
            var thisServerPortLen = BitConverter.GetBytes(thisServerPortData.Length);

            var messageWrapper = new List<byte>();
            messageWrapper.AddRange(requestFlag);
            messageWrapper.AddRange(messageDataLen);
            messageWrapper.AddRange(messageData);
            messageWrapper.AddRange(thisServerIpLen);
            messageWrapper.AddRange(thisServerIpData);
            messageWrapper.AddRange(thisServerPortLen);
            messageWrapper.AddRange(thisServerPortData);

            return messageWrapper.ToArray();
        }

        public static byte[] ConstructInboundFileTransferRequest(string remoteFilePath, string localIpAddress, int localPort, string localFolderPath)
        {
            var requestFlag = BitConverter.GetBytes((int)TransferType.OutboundFileTransfer);
            var remoteFilePathData = Encoding.UTF8.GetBytes(remoteFilePath);
            var remoteFilePathLen = BitConverter.GetBytes(remoteFilePathData.Length);
            var thisServerIpData = Encoding.UTF8.GetBytes(localIpAddress);
            var thisServerIpLen = BitConverter.GetBytes(thisServerIpData.Length);
            var thisServerPortData = Encoding.UTF8.GetBytes(localPort.ToString(CultureInfo.InvariantCulture));
            var thisServerPortLen = BitConverter.GetBytes(thisServerPortData.Length);
            var targetDirData = Encoding.UTF8.GetBytes(localFolderPath);
            var targetDirLen = BitConverter.GetBytes(targetDirData.Length);

            var messageWrapper = new List<byte>();
            messageWrapper.AddRange(requestFlag);
            messageWrapper.AddRange(remoteFilePathLen);
            messageWrapper.AddRange(remoteFilePathData);
            messageWrapper.AddRange(thisServerIpLen);
            messageWrapper.AddRange(thisServerIpData);
            messageWrapper.AddRange(thisServerPortLen);
            messageWrapper.AddRange(thisServerPortData);            
            messageWrapper.AddRange(targetDirLen);
            messageWrapper.AddRange(targetDirData);

            return messageWrapper.ToArray();
        }

        public static byte[] ConstructOutboundFileTransferRequest(string localFilePath, long fileSizeBytes, string remoteFolderPath)
        {
            var requestFlag = BitConverter.GetBytes((int)TransferType.InboundFileTransfer);
            var fileName = Path.GetFileName(localFilePath);
            var fileNameData = Encoding.UTF8.GetBytes(fileName);
            var fileNameLen = BitConverter.GetBytes(fileNameData.Length);
            var sizeBytesData = Encoding.UTF8.GetBytes(fileSizeBytes.ToString(CultureInfo.InvariantCulture));
            var sizeBytesLen = BitConverter.GetBytes(sizeBytesData.Length);
            var targetDirData = Encoding.UTF8.GetBytes(remoteFolderPath);
            var targetDirLen = BitConverter.GetBytes(targetDirData.Length);

            var messageWrapper = new List<byte>();
            messageWrapper.AddRange(requestFlag);
            messageWrapper.AddRange(fileNameLen);
            messageWrapper.AddRange(fileNameData);
            messageWrapper.AddRange(sizeBytesLen);
            messageWrapper.AddRange(sizeBytesData);
            messageWrapper.AddRange(targetDirLen);
            messageWrapper.AddRange(targetDirData);

            return messageWrapper.ToArray();
        }
    }
}
