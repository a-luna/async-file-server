

namespace TplSocketServer
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;

    internal class MessageUnwrapper
    {
        public const int SizeOfInt32InBytes = 4;
        public const int SizeOfCharInBytes = 2;

        public static int ReadInt32(byte[] buffer)
        {
            return BitConverter.ToInt32(buffer, 0);
        }

        public static (string message, string remoteIpAddress, int remotePortNumber) ReadTextMessage(byte[] buffer)
        {
            var messageLen = BitConverter.ToInt32(buffer, SizeOfInt32InBytes);
            var message = Encoding.UTF8.GetString(buffer, SizeOfInt32InBytes * 2, messageLen);

            var clientIpLen = BitConverter.ToInt32(buffer, (SizeOfInt32InBytes * 2) + messageLen);
            var clientIp = Encoding.UTF8.GetString(buffer, (SizeOfInt32InBytes * 3) + messageLen, clientIpLen);

            var clientPortLen = BitConverter.ToInt32(buffer, (SizeOfInt32InBytes * 3) + messageLen + clientIpLen);
            var clientPort = int.Parse(Encoding.UTF8.GetString(buffer, (SizeOfInt32InBytes * 4) + messageLen + clientIpLen, clientPortLen));

            return (message, clientIp, clientPort);
        }

        public static (string filePath, long fileSizeInBytes, string remoteIpAddress, int remotePort) ReadInboundFileTransferRequest(byte[] buffer)
        {
            var fileNameLen = BitConverter.ToInt32(buffer, SizeOfInt32InBytes);
            var fileName = Encoding.UTF8.GetString(buffer, SizeOfInt32InBytes * 2, fileNameLen);

            var fileSizeLen = BitConverter.ToInt32(buffer, (SizeOfInt32InBytes * 2) + fileNameLen);
            var fileSize = Convert.ToInt64( Encoding.UTF8.GetString(buffer, (SizeOfInt32InBytes * 3) + fileNameLen, fileSizeLen));

            var remoteIpLen = BitConverter.ToInt32(buffer, (SizeOfInt32InBytes * 3) + fileNameLen + fileSizeLen);
            var remoteIpAddress = Encoding.UTF8.GetString(buffer, (SizeOfInt32InBytes * 4) + fileNameLen + fileSizeLen, remoteIpLen);

            var remotePortLen = BitConverter.ToInt32(buffer, (SizeOfInt32InBytes * 4) + fileNameLen + fileSizeLen + remoteIpLen);
            var remotePort = int.Parse(Encoding.UTF8.GetString(buffer, (SizeOfInt32InBytes * 5) + fileNameLen + fileSizeLen + remoteIpLen, remotePortLen));

            var targetFolderLen = BitConverter.ToInt32(buffer, (SizeOfInt32InBytes * 5) + fileNameLen + fileSizeLen + remoteIpLen + remotePortLen);
            var targerFolder = Encoding.UTF8.GetString( buffer, (SizeOfInt32InBytes * 6) + fileNameLen + fileSizeLen + remoteIpLen + remotePortLen, targetFolderLen);

            var localFilePath = Path.Combine(targerFolder, fileName);

            return (localFilePath, fileSize, remoteIpAddress, remotePort);
        }

        public static (string localFilePath, string remoteIpAddress, int remotePortNumber, string remoteFilePath) ReadOutboundFileTransferRequest(byte[] buffer)
        {
            var localFilePathLen = BitConverter.ToInt32(buffer, SizeOfInt32InBytes);
            var localFilePath = Encoding.UTF8.GetString(buffer, SizeOfInt32InBytes * 2, localFilePathLen);

            var clientIpLen = BitConverter.ToInt32(buffer, (SizeOfInt32InBytes * 2) + localFilePathLen);
            var clientIp = Encoding.UTF8.GetString( buffer, (SizeOfInt32InBytes * 3) + localFilePathLen, clientIpLen);

            var clientPortLen = BitConverter.ToInt32( buffer, (SizeOfInt32InBytes * 3) + localFilePathLen + clientIpLen);
            var clientPort = int.Parse( Encoding.UTF8.GetString( buffer, (SizeOfInt32InBytes * 4) + localFilePathLen + clientIpLen, clientPortLen));

            var remoteFolderData = BitConverter.ToInt32( buffer, (SizeOfInt32InBytes * 4) + localFilePathLen + clientIpLen + clientPortLen);
            var remoteFolder = Encoding.UTF8.GetString( buffer, (SizeOfInt32InBytes * 5) + localFilePathLen + clientIpLen + clientPortLen, remoteFolderData);

            return (localFilePath, clientIp, clientPort, remoteFolder);
        }

        public static (string remoteIpAddress, int remotePortNumber, string remoteFolderPath) ReadFileListRequest(byte[] buffer)
        {
            var remoteIpAddressLen = BitConverter.ToInt32(buffer, SizeOfInt32InBytes);
            var remoteIp = Encoding.UTF8.GetString(buffer, SizeOfInt32InBytes * 2, remoteIpAddressLen);

            var remotePortNumberLen = BitConverter.ToInt32(buffer, (SizeOfInt32InBytes * 2) + remoteIpAddressLen);
            var remotePort = int.Parse(Encoding.UTF8.GetString(buffer, (SizeOfInt32InBytes * 3) + remoteIpAddressLen, remotePortNumberLen));

            var remoteFolderPathLen = BitConverter.ToInt32(buffer, (SizeOfInt32InBytes * 3) + remoteIpAddressLen + remotePortNumberLen);
            var remoteFolder = Encoding.UTF8.GetString(buffer, (SizeOfInt32InBytes * 4) + remoteIpAddressLen + remotePortNumberLen, remoteFolderPathLen);

            return (remoteIp, remotePort, remoteFolder);
        }

        public static (string requestorIpAddress, int requestortPort, string clientIpAddress, int clientPort, string targetFolder, List<(string filePath, long fileSize)> fileInfo) ReadFileListResponse(byte[] buffer)
        {
            var remoteServerIpLen = BitConverter.ToInt32(buffer, SizeOfInt32InBytes);
            var remoteIp = Encoding.UTF8.GetString(buffer, SizeOfInt32InBytes * 2, remoteServerIpLen);

            var remoteServerPortLen = BitConverter.ToInt32(buffer, (SizeOfInt32InBytes * 2) + remoteServerIpLen);
            var remotePort = int.Parse(Encoding.UTF8.GetString(buffer, (SizeOfInt32InBytes * 3) + remoteServerIpLen, remoteServerPortLen));

            var localIpLen = BitConverter.ToInt32(buffer, (SizeOfInt32InBytes * 3) + remoteServerIpLen + remoteServerPortLen);
            var localIp = Encoding.UTF8.GetString(buffer, (SizeOfInt32InBytes * 4) + remoteServerIpLen + remoteServerPortLen, localIpLen);

            var localPortLen = BitConverter.ToInt32(buffer, (SizeOfInt32InBytes * 4) + remoteServerIpLen + remoteServerPortLen + localIpLen);
            var localPort = int.Parse(Encoding.UTF8.GetString(buffer, (SizeOfInt32InBytes * 5) + remoteServerIpLen + remoteServerPortLen + localIpLen, localPortLen));

            var targetFolderLen = BitConverter.ToInt32(buffer, (SizeOfInt32InBytes * 5) + remoteServerIpLen + remoteServerPortLen + localIpLen + localPortLen);
            var targetFolder = Encoding.UTF8.GetString(buffer, (SizeOfInt32InBytes * 6) + remoteServerIpLen + remoteServerPortLen + localIpLen + localPortLen, targetFolderLen);

            var fileInfoLen = BitConverter.ToInt32(buffer, (SizeOfInt32InBytes * 6) + remoteServerIpLen + remoteServerPortLen + localIpLen + localPortLen + targetFolderLen);
            var fileInfo = Encoding.UTF8.GetString(buffer, (SizeOfInt32InBytes * 7) + remoteServerIpLen + remoteServerPortLen + localIpLen + localPortLen + targetFolderLen, fileInfoLen);

            var fileInfoSeparatorLen = BitConverter.ToInt32(buffer, (SizeOfInt32InBytes * 7) + remoteServerIpLen + remoteServerPortLen + localIpLen + localPortLen + targetFolderLen + fileInfoLen);
            var fileInfoSeparator = Encoding.UTF8.GetString(buffer, (SizeOfInt32InBytes * 8) + remoteServerIpLen + remoteServerPortLen + localIpLen + localPortLen + targetFolderLen + fileInfoLen, fileInfoSeparatorLen);

            var fileSeparatorLen = BitConverter.ToInt32(buffer, (SizeOfInt32InBytes * 8) + remoteServerIpLen + remoteServerPortLen + localIpLen + localPortLen + targetFolderLen + fileInfoLen + fileInfoSeparatorLen);
            var fileSeparator = Encoding.UTF8.GetString(buffer, (SizeOfInt32InBytes * 9) + remoteServerIpLen + remoteServerPortLen + localIpLen + localPortLen + targetFolderLen + fileInfoLen + fileInfoSeparatorLen, fileSeparatorLen);

            var fileInfoList = new List<(string filePath, long fileSize)>();
            var split = fileInfo.Split(fileSeparator).ToList();

            foreach (var infoString in split)
            {
                var infoSplit = infoString.Split(fileInfoSeparator);
                if (infoSplit.Length == 2)
                {
                    var filePath = infoSplit[0];
                    fileInfoList.Add(long.TryParse(infoSplit[1], out var fileSizeBytes)
                        ? (filePath, fileSizeBytes)
                        : (filePath, 0));
                }
            }

            return (remoteIp, remotePort, localIp, localPort, targetFolder, fileInfoList);
        }

        public static (string remoteIpAddress, int remotePortNumber) ReadTransferFolderRequest(byte[] buffer)
        {
            var remoteIpAddressLen = BitConverter.ToInt32(buffer, SizeOfInt32InBytes);
            var remoteIp = Encoding.UTF8.GetString(buffer, SizeOfInt32InBytes * 2, remoteIpAddressLen);

            var remotePortNumberLen = BitConverter.ToInt32(buffer, (SizeOfInt32InBytes * 2) + remoteIpAddressLen);
            var remotePort = int.Parse(Encoding.UTF8.GetString(buffer, (SizeOfInt32InBytes * 3) + remoteIpAddressLen, remotePortNumberLen));
            
            return (remoteIp, remotePort);
        }

        public static (string remoteIpAddress, int remotePortNumber, string remoteFolderPath) ReadTransferFolderResponse(byte[] buffer)
        {
            var remoteIpAddressLen = BitConverter.ToInt32(buffer, SizeOfInt32InBytes);
            var remoteIp = Encoding.UTF8.GetString(buffer, SizeOfInt32InBytes * 2, remoteIpAddressLen);

            var remotePortNumberLen = BitConverter.ToInt32(buffer, (SizeOfInt32InBytes * 2) + remoteIpAddressLen);
            var remotePort = int.Parse(Encoding.UTF8.GetString(buffer, (SizeOfInt32InBytes * 3) + remoteIpAddressLen, remotePortNumberLen));

            var remoteFolderPathLen = BitConverter.ToInt32(buffer, (SizeOfInt32InBytes * 3) + remoteIpAddressLen + remotePortNumberLen);
            var remoteFolder = Encoding.UTF8.GetString(buffer, (SizeOfInt32InBytes * 4) + remoteIpAddressLen + remotePortNumberLen, remoteFolderPathLen);

            return (remoteIp, remotePort, remoteFolder);
        }

        public static (string remoteIpAddress, int remotePortNumber) ReadPublicIpAddressRequest(byte[] buffer)
        {
            var remoteIpAddressLen = BitConverter.ToInt32(buffer, SizeOfInt32InBytes);
            var remoteIp = Encoding.UTF8.GetString(buffer, SizeOfInt32InBytes * 2, remoteIpAddressLen);

            var remotePortNumberLen = BitConverter.ToInt32(buffer, (SizeOfInt32InBytes * 2) + remoteIpAddressLen);
            var remotePort = int.Parse(Encoding.UTF8.GetString(buffer, (SizeOfInt32InBytes * 3) + remoteIpAddressLen, remotePortNumberLen));

            return (remoteIp, remotePort);
        }

        public static (string remoteIpAddress, int remotePortNumber, string publicIpAddress) ReadPublicIpAddressResponse(byte[] buffer)
        {
            var remoteIpAddressLen = BitConverter.ToInt32(buffer, SizeOfInt32InBytes);
            var remoteIp = Encoding.UTF8.GetString(buffer, SizeOfInt32InBytes * 2, remoteIpAddressLen);

            var remotePortNumberLen = BitConverter.ToInt32(buffer, (SizeOfInt32InBytes * 2) + remoteIpAddressLen);
            var remotePort = int.Parse(Encoding.UTF8.GetString(buffer, (SizeOfInt32InBytes * 3) + remoteIpAddressLen, remotePortNumberLen));

            var publicIpLen = BitConverter.ToInt32(buffer, (SizeOfInt32InBytes * 3) + remoteIpAddressLen + remotePortNumberLen);
            var publicIp = Encoding.UTF8.GetString(buffer, (SizeOfInt32InBytes * 4) + remoteIpAddressLen + remotePortNumberLen, publicIpLen);

            return (remoteIp, remotePort, publicIp);
        }
    }
}
