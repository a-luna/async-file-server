using System.Collections.Generic;
using System.Linq;

namespace TplSocketServer
{
    using System;
    using System.IO;
    using System.Text;

    internal class MessageUnwrapper
    {
        public const int SizeOfInt32InBytes = 4;
        public const int SizeOfCharInBytes = 2;

        public static int DetermineTransferType(byte[] buffer)
        {
            return BitConverter.ToInt32(buffer, 0);
        }

        public static (string message, string remoteIpAddress, int remotePortNumber) ReadTextMessageRequest(byte[] buffer)
        {
            int messageDataLen = BitConverter.ToInt32(buffer, SizeOfInt32InBytes);
            var message = Encoding.UTF8.GetString(buffer, SizeOfInt32InBytes * 2, messageDataLen);

            int clientIpLen = BitConverter.ToInt32(buffer, (SizeOfInt32InBytes * 2) + messageDataLen);
            var clientIpAddress = Encoding.UTF8.GetString(buffer, (SizeOfInt32InBytes * 3) + messageDataLen, clientIpLen);

            int clientPortLen = BitConverter.ToInt32(buffer, (SizeOfInt32InBytes * 3) + messageDataLen + clientIpLen);
            var clientPortNumber = int.Parse(Encoding.UTF8.GetString(buffer, (SizeOfInt32InBytes * 4) + messageDataLen + clientIpLen, clientPortLen));

            return (message, clientIpAddress, clientPortNumber);
        }

        public static (string filePath, long fileSizeInBytes) ReadInboundFileTransferRequest(byte[] buffer)
        {
            int fileNameLen = BitConverter.ToInt32(buffer, SizeOfInt32InBytes);
            var fileName = Encoding.UTF8.GetString(buffer, SizeOfInt32InBytes * 2, fileNameLen);

            int fileSizeLen = BitConverter.ToInt32(buffer, (SizeOfInt32InBytes * 2) + fileNameLen);
            var fileSizeInBytes = Convert.ToInt64(
                Encoding.UTF8.GetString(buffer, (SizeOfInt32InBytes * 3) + fileNameLen, fileSizeLen));

            int targetDirLen = BitConverter.ToInt32(buffer, (SizeOfInt32InBytes * 3) + fileNameLen + fileSizeLen);
            var localFolderPath = Encoding.UTF8.GetString(
                buffer,
                (SizeOfInt32InBytes * 4) + fileNameLen + fileSizeLen,
                targetDirLen);

            var localFilePath = Path.Combine(localFolderPath, fileName);

            return (localFilePath, fileSizeInBytes);
        }

        public static (string localFilePath, string remoteIpAddress, int remotePortNumber, string remoteFilePath) ReadOutboundFileTransferRequest(byte[] buffer)
        {
            int requestedFilePathLen = BitConverter.ToInt32(buffer, SizeOfInt32InBytes);
            var localFilePath = Encoding.UTF8.GetString(buffer, SizeOfInt32InBytes * 2, requestedFilePathLen);
            
            var fileName = Path.GetFileName(localFilePath);

            int clientIpLen = BitConverter.ToInt32(buffer, (SizeOfInt32InBytes * 2) + requestedFilePathLen);

            var clientIpAddress = Encoding.UTF8.GetString(
                buffer,
                (SizeOfInt32InBytes * 3) + requestedFilePathLen,
                clientIpLen);

            int clientPortLen = BitConverter.ToInt32(
                buffer,
                (SizeOfInt32InBytes * 3) + requestedFilePathLen + clientIpLen);

            var clientPortNumber = int.Parse(
                Encoding.UTF8.GetString(
                    buffer,
                    (SizeOfInt32InBytes * 4) + requestedFilePathLen + clientIpLen,
                    clientPortLen));

            int clientFolderLen = BitConverter.ToInt32(
                buffer,
                (SizeOfInt32InBytes * 4) + requestedFilePathLen + clientIpLen + clientPortLen);

            var remoteFolderPath = Encoding.UTF8.GetString(
                buffer,
                (SizeOfInt32InBytes * 5) + requestedFilePathLen + clientIpLen + clientPortLen,
                clientFolderLen);

            return (localFilePath, clientIpAddress, clientPortNumber, remoteFolderPath);
        }

        public static (string remoteIpAddress, int remotePortNumber) ReadFileListRequest(byte[] buffer)
        {
            int remoteIpAddressLen = BitConverter.ToInt32(buffer, SizeOfInt32InBytes);
            var remoteIpAddress = Encoding.UTF8.GetString(buffer, SizeOfInt32InBytes * 2, remoteIpAddressLen);

            int remotePortNumberLen = BitConverter.ToInt32(buffer, (SizeOfInt32InBytes * 2) + remoteIpAddressLen);
            var remotePortNumber = int.Parse(Encoding.UTF8.GetString(buffer, (SizeOfInt32InBytes * 3) + remoteIpAddressLen, remotePortNumberLen));

            return (remoteIpAddress, remotePortNumber);
        }

        public static (string remoteServerIp, int remoteServerPort, List<(string filePath, long fileSize)> fileInfo) ReadFileListResponse(byte[] buffer)
        {
            int remoteServerIpLen = BitConverter.ToInt32(buffer, SizeOfInt32InBytes);
            var remoteServerIp = Encoding.UTF8.GetString(buffer, SizeOfInt32InBytes * 2, remoteServerIpLen);

            int remoteServerPortLen = BitConverter.ToInt32(buffer, (SizeOfInt32InBytes * 2) + remoteServerIpLen);
            var remoteServerPort = int.Parse(Encoding.UTF8.GetString(buffer, (SizeOfInt32InBytes * 3) + remoteServerIpLen, remoteServerPortLen));

            int fileInfoLen =
                BitConverter.ToInt32(buffer, (SizeOfInt32InBytes * 3) + remoteServerIpLen + remoteServerPortLen);

            var fileInfo = Encoding.UTF8.GetString(buffer,
                (SizeOfInt32InBytes * 4) + remoteServerIpLen + remoteServerPortLen, fileInfoLen);

            var fileSeparaorChar = Encoding.UTF8.GetChars(buffer,
                (SizeOfInt32InBytes * 4) + remoteServerIpLen + remoteServerPortLen + fileInfoLen, 1);

            var fileSizeSeparatorChar = Encoding.UTF8.GetChars(buffer,
                (SizeOfInt32InBytes * 4) + remoteServerIpLen + remoteServerPortLen + fileInfoLen + SizeOfCharInBytes,
                1);

            var fileInfoList = new List<(string filePath, long fileSize)>();
            var fileInfoSplit = fileInfo.Split(fileSizeSeparatorChar).ToList();

            foreach (var info in fileInfoSplit)
            {
                var infoSplit = info.Split(fileSeparaorChar);
                if (infoSplit.Length == 2)
                {
                    var filePath = infoSplit[0];
                    if (long.TryParse(infoSplit[1], out long fileSizeBytes))
                    {
                        fileInfoList.Add((filePath, fileSizeBytes));
                    }
                }
            }

            return (remoteServerIp, remoteServerPort, fileInfoList);
        }
    }
}
