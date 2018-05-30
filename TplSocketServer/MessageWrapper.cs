namespace TplSockets
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text;

    using AaronLuna.Common.Extensions;

    static class MessageWrapper
    {
        public const int SizeOfInt32InBytes = 4;
        public const int SizeOfCharInBytes = 2;

        public static byte[] ConstructBasicRequest(MessageType messageType, string localIpAddress, int localPort)
        {
            var requestType = BitConverter.GetBytes((int)messageType);
            var thisServerIpData = Encoding.UTF8.GetBytes(localIpAddress);
            var thisServerIpLen = BitConverter.GetBytes(thisServerIpData.Length);
            var thisServerPortData = Encoding.UTF8.GetBytes(localPort.ToString(CultureInfo.InvariantCulture));
            var thisServerPortLen = BitConverter.GetBytes(thisServerPortData.Length);

            var messageWrapper = new List<byte>();
            messageWrapper.AddRange(requestType);
            messageWrapper.AddRange(thisServerIpLen);
            messageWrapper.AddRange(thisServerIpData);
            messageWrapper.AddRange(thisServerPortLen);
            messageWrapper.AddRange(thisServerPortData);

            return messageWrapper.ToArray();
        }

        public static byte[] ConstructRequestWithStringValue(
            MessageType messageType,
            string localIpAddress,
            int localPort,
            string message)
        {
            var requestType = BitConverter.GetBytes((int)messageType);
            var thisServerIpData = Encoding.UTF8.GetBytes(localIpAddress);
            var thisServerIpLen = BitConverter.GetBytes(thisServerIpData.Length);
            var thisServerPortData = Encoding.UTF8.GetBytes(localPort.ToString(CultureInfo.InvariantCulture));
            var thisServerPortLen = BitConverter.GetBytes(thisServerPortData.Length);
            var messageData = Encoding.UTF8.GetBytes(message);
            var messageLen = BitConverter.GetBytes(messageData.Length);

            var messageWrapper = new List<byte>();
            messageWrapper.AddRange(requestType);
            messageWrapper.AddRange(thisServerIpLen);
            messageWrapper.AddRange(thisServerIpData);
            messageWrapper.AddRange(thisServerPortLen);
            messageWrapper.AddRange(thisServerPortData);
            messageWrapper.AddRange(messageLen);
            messageWrapper.AddRange(messageData);

            return messageWrapper.ToArray();
        }

        public static byte[] ConstructInboundFileTransferRequest(
            string remoteFilePath,
            string localIpAddress,
            int localPort,
            string localFolderPath)
        {
            var requestType = BitConverter.GetBytes((int) MessageType.OutboundFileTransferRequest);
            var remoteFilePathData = Encoding.UTF8.GetBytes(remoteFilePath);
            var remoteFilePathLen = BitConverter.GetBytes(remoteFilePathData.Length);
            var thisServerIpData = Encoding.UTF8.GetBytes(localIpAddress);
            var thisServerIpLen = BitConverter.GetBytes(thisServerIpData.Length);
            var thisServerPortData = Encoding.UTF8.GetBytes(localPort.ToString(CultureInfo.InvariantCulture));
            var thisServerPortLen = BitConverter.GetBytes(thisServerPortData.Length);
            var targetDirData = Encoding.UTF8.GetBytes(localFolderPath);
            var targetDirLen = BitConverter.GetBytes(targetDirData.Length);

            var messageWrapper = new List<byte>();
            messageWrapper.AddRange(requestType);
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

        public static byte[] ConstructOutboundFileTransferRequest(
            string localFilePath,
            long fileSizeBytes,
            string remoteIpAddress,
            int remotePort,
            string remoteFolderPath)
        {
            var requestType = BitConverter.GetBytes((int) MessageType.InboundFileTransferRequest);

            var fileName = Path.GetFileName(localFilePath);
            var fileNameData = Encoding.UTF8.GetBytes(fileName);
            var fileNameLen = BitConverter.GetBytes(fileNameData.Length);

            var sizeBytesData = Encoding.UTF8.GetBytes(fileSizeBytes.ToString(CultureInfo.InvariantCulture));
            var sizeBytesLen = BitConverter.GetBytes(sizeBytesData.Length);

            var remoteIpData = Encoding.UTF8.GetBytes(remoteIpAddress);
            var remoteIpLen = BitConverter.GetBytes(remoteIpData.Length);

            var remotePortData = Encoding.UTF8.GetBytes(remotePort.ToString(CultureInfo.InvariantCulture));
            var remotePortLen = BitConverter.GetBytes(remotePortData.Length);

            var targetDirData = Encoding.UTF8.GetBytes(remoteFolderPath);
            var targetDirLen = BitConverter.GetBytes(targetDirData.Length);

            var messageWrapper = new List<byte>();
            messageWrapper.AddRange(requestType);
            messageWrapper.AddRange(fileNameLen);
            messageWrapper.AddRange(fileNameData);
            messageWrapper.AddRange(sizeBytesLen);
            messageWrapper.AddRange(sizeBytesData);
            messageWrapper.AddRange(remoteIpLen);
            messageWrapper.AddRange(remoteIpData);
            messageWrapper.AddRange(remotePortLen);
            messageWrapper.AddRange(remotePortData);
            messageWrapper.AddRange(targetDirLen);
            messageWrapper.AddRange(targetDirData);

            return messageWrapper.ToArray();
        }
        
        public static byte[] ConstructFileListResponse(
            List<(string filePath, long fileSizeBytes)> fileInfoList,
            string fileInfoSeparator,
            string fileSeparator,
            string localIpAddress,
            int localPort,
            string remoteFolderPath)
        {
            var requestType = BitConverter.GetBytes((int) MessageType.FileListResponse);

            var localServerIpData = Encoding.UTF8.GetBytes(localIpAddress);
            var localServerIpLen = BitConverter.GetBytes(localServerIpData.Length);

            var localServerPortData = Encoding.UTF8.GetBytes(localPort.ToString(CultureInfo.InvariantCulture));
            var localServerPortLen = BitConverter.GetBytes(localServerPortData.Length);

            var requestorFolderPathData = Encoding.UTF8.GetBytes(remoteFolderPath);
            var requestorFolderPathLen = BitConverter.GetBytes(requestorFolderPathData.Length);

            var messageWrapper = new List<byte>();
            messageWrapper.AddRange(requestType);
            messageWrapper.AddRange(localServerIpLen);
            messageWrapper.AddRange(localServerIpData);
            messageWrapper.AddRange(localServerPortLen);
            messageWrapper.AddRange(localServerPortData);
            messageWrapper.AddRange(requestorFolderPathLen);
            messageWrapper.AddRange(requestorFolderPathData);

            var allFileInfo = string.Empty;
            foreach (var i in Enumerable.Range(0, fileInfoList.Count))
            {
                var fileInfoString = $"{fileInfoList[i].filePath}{fileInfoSeparator}{fileInfoList[i].fileSizeBytes}";

                allFileInfo += fileInfoString;

                if (!i.IsLastIteration(fileInfoList.Count))
                {
                    allFileInfo += fileSeparator;
                }
            }

            var fileInfoListData = Encoding.UTF8.GetBytes(allFileInfo);
            var fileInfoListLen = BitConverter.GetBytes(fileInfoListData.Length);
            var fileInfoSeparatorData = Encoding.UTF8.GetBytes(fileInfoSeparator);
            var fileInfoSeparatorLen = BitConverter.GetBytes(fileInfoSeparatorData.Length);
            var fileSeparatorData = Encoding.UTF8.GetBytes(fileSeparator);
            var fileSeparatorLen = BitConverter.GetBytes(fileSeparatorData.Length);

            messageWrapper.AddRange(fileInfoListLen);
            messageWrapper.AddRange(fileInfoListData);
            messageWrapper.AddRange(fileInfoSeparatorLen);
            messageWrapper.AddRange(fileInfoSeparatorData);
            messageWrapper.AddRange(fileSeparatorLen);
            messageWrapper.AddRange(fileSeparatorData);

            return messageWrapper.ToArray();
        }
        
        public static byte[] ConstructServerInfoResponse(
            string localIpAddress,
            int localPort,
            string publicIp,
            string transferFolder)
        {
            var requestType = BitConverter.GetBytes((int)MessageType.ServerInfoResponse);
            var thisServerIpData = Encoding.UTF8.GetBytes(localIpAddress);
            var thisServerIpLen = BitConverter.GetBytes(thisServerIpData.Length);
            var thisServerPortData = Encoding.UTF8.GetBytes(localPort.ToString(CultureInfo.InvariantCulture));
            var thisServerPortLen = BitConverter.GetBytes(thisServerPortData.Length);
            var publicIpData = Encoding.UTF8.GetBytes(publicIp);
            var publicIpLen = BitConverter.GetBytes(publicIpData.Length);
            var transferFolderData = Encoding.UTF8.GetBytes(transferFolder);
            var transferFolderLen = BitConverter.GetBytes(transferFolderData.Length);

            var messageWrapper = new List<byte>();
            messageWrapper.AddRange(requestType);
            messageWrapper.AddRange(thisServerIpLen);
            messageWrapper.AddRange(thisServerIpData);
            messageWrapper.AddRange(thisServerPortLen);
            messageWrapper.AddRange(thisServerPortData);
            messageWrapper.AddRange(publicIpLen);
            messageWrapper.AddRange(publicIpData);
            messageWrapper.AddRange(transferFolderLen);
            messageWrapper.AddRange(transferFolderData);

            return messageWrapper.ToArray();
        }
    }
}
