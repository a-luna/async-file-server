namespace TplSockets
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Text;

    static class RequestUnwrapper
    {
        public const int SizeOfInt32InBytes = 4;
        public const int SizeOfCharInBytes = 2;

        public static int ReadInt32(byte[] requestData)
        {
            return BitConverter.ToInt32(requestData, 0);
        }

        public static (string message, string remoteIpAddress, int remotePortNumber)
            ReadTextMessage(byte[] requestData)
        {
            var messageLen = BitConverter.ToInt32(requestData, SizeOfInt32InBytes);
            var message = Encoding.UTF8.GetString(requestData, SizeOfInt32InBytes * 2, messageLen);

            var clientIpLen = BitConverter.ToInt32(requestData, SizeOfInt32InBytes * 2 + messageLen);
            var clientIp = Encoding.UTF8.GetString(requestData, SizeOfInt32InBytes * 3 + messageLen, clientIpLen);

            var clientPortLen = BitConverter.ToInt32(requestData, SizeOfInt32InBytes * 3 + messageLen + clientIpLen);
            var clientPort = int.Parse(Encoding.UTF8.GetString(requestData, SizeOfInt32InBytes * 4 + messageLen + clientIpLen, clientPortLen));

            return (message, clientIp, clientPort);
        }

        public static (
            long responseCode,
            FileTransferInitiator initiator,
            int transferid,
            string filePath,
            long fileSizeInBytes,
            string remoteIpAddress,
            int remotePort)
            ReadInboundFileTransferRequest(byte[] requestData)
        {
            var responseCodeLen = BitConverter.ToInt32(requestData, SizeOfInt32InBytes);
            var responseCode = BitConverter.ToInt64(requestData, SizeOfInt32InBytes * 2);
            
            var transferInitiatorLen = BitConverter.ToInt32(requestData, SizeOfInt32InBytes * 2 + responseCodeLen);
            var transferInitiatorData = BitConverter.ToInt32(requestData, SizeOfInt32InBytes * 3 + responseCodeLen).ToString();
            var transferInitiator = (FileTransferInitiator)Enum.Parse(typeof(FileTransferInitiator), transferInitiatorData);

            var transferIdLen = BitConverter.ToInt32(requestData, SizeOfInt32InBytes * 3 + responseCodeLen + transferInitiatorLen);
            var transferId = BitConverter.ToInt32(requestData, SizeOfInt32InBytes * 4 + responseCodeLen + transferInitiatorLen);

            var fileNameLen = BitConverter.ToInt32(requestData, SizeOfInt32InBytes * 4 + responseCodeLen + transferInitiatorLen + transferIdLen);
            var fileName = Encoding.UTF8.GetString(requestData, SizeOfInt32InBytes * 5 + responseCodeLen + transferInitiatorLen + transferIdLen, fileNameLen);

            var fileSizeLen = BitConverter.ToInt32(requestData, SizeOfInt32InBytes * 5 + responseCodeLen + transferInitiatorLen + transferIdLen + fileNameLen);
            var fileSize = BitConverter.ToInt64(requestData, SizeOfInt32InBytes * 6 + responseCodeLen + transferInitiatorLen + transferIdLen + fileNameLen);

            var remoteIpLen = BitConverter.ToInt32(requestData, SizeOfInt32InBytes * 6 + responseCodeLen + transferInitiatorLen + transferIdLen + fileNameLen + fileSizeLen);
            var remoteIpAddress = Encoding.UTF8.GetString(requestData, SizeOfInt32InBytes * 7 + responseCodeLen + transferInitiatorLen + transferIdLen + fileNameLen + fileSizeLen, remoteIpLen);

            var remotePortLen = BitConverter.ToInt32(requestData, SizeOfInt32InBytes * 7 + responseCodeLen + transferInitiatorLen + transferIdLen + fileNameLen + fileSizeLen + remoteIpLen);
            var remotePort = BitConverter.ToInt32(requestData, SizeOfInt32InBytes * 8 + responseCodeLen + transferInitiatorLen + transferIdLen + fileNameLen + fileSizeLen + remoteIpLen);

            var targetFolderLen = BitConverter.ToInt32(requestData, SizeOfInt32InBytes * 8 + responseCodeLen + transferInitiatorLen + transferIdLen + fileNameLen + fileSizeLen + remoteIpLen + remotePortLen);
            var targerFolder = Encoding.UTF8.GetString( requestData, SizeOfInt32InBytes * 9 + responseCodeLen + transferInitiatorLen + transferIdLen + fileNameLen + fileSizeLen + remoteIpLen + remotePortLen, targetFolderLen);

            var localFilePath = Path.Combine(targerFolder, fileName);

            return (responseCode, transferInitiator, transferId, localFilePath, fileSize, remoteIpAddress, remotePort);
        }

        public static (
            int remoteServerTransferId,
            string localFilePath,
            string remoteServerIpAddress,
            int remoteServerPortNumber,
            string remoteFilePath)
            ReadOutboundFileTransferRequest(byte[] requestData)
        {
            var remoteServerTransferIdLen = BitConverter.ToInt32(requestData, SizeOfInt32InBytes);
            var remoteServerTransferId = BitConverter.ToInt32(requestData, SizeOfInt32InBytes * 2);

            var localFilePathLen = BitConverter.ToInt32(requestData, SizeOfInt32InBytes * 2 + remoteServerTransferIdLen);
            var localFilePath = Encoding.UTF8.GetString(requestData, SizeOfInt32InBytes * 3 + remoteServerTransferIdLen, localFilePathLen);

            var remoteServerIpLen = BitConverter.ToInt32(requestData, SizeOfInt32InBytes * 3 + remoteServerTransferIdLen + localFilePathLen);
            var remoteServerIp = Encoding.UTF8.GetString( requestData, SizeOfInt32InBytes * 4 + remoteServerTransferIdLen + localFilePathLen, remoteServerIpLen);

            var remoteServerPortLen = BitConverter.ToInt32( requestData, SizeOfInt32InBytes * 4 + remoteServerTransferIdLen + localFilePathLen + remoteServerIpLen);
            var remoteServerPort = BitConverter.ToInt32(requestData, SizeOfInt32InBytes * 5 + remoteServerTransferIdLen + localFilePathLen + remoteServerIpLen);

            var remoteFolderData = BitConverter.ToInt32( requestData, SizeOfInt32InBytes * 5 + remoteServerTransferIdLen + localFilePathLen + remoteServerIpLen + remoteServerPortLen);
            var remoteFolder = Encoding.UTF8.GetString( requestData, SizeOfInt32InBytes * 6 + remoteServerTransferIdLen + localFilePathLen + remoteServerIpLen + remoteServerPortLen, remoteFolderData);

            return (remoteServerTransferId, localFilePath, remoteServerIp, remoteServerPort, remoteFolder);
        }

        public static (string remoteIpAddress, int remotePortNumber, string remoteFolderPath)
            ReadFileListRequest(byte[] requestData)
        {
            var remoteIpAddressLen = BitConverter.ToInt32(requestData, SizeOfInt32InBytes);
            var remoteIp = Encoding.UTF8.GetString(requestData, SizeOfInt32InBytes * 2, remoteIpAddressLen);

            var remotePortNumberLen = BitConverter.ToInt32(requestData, SizeOfInt32InBytes * 2 + remoteIpAddressLen);
            var remotePort = int.Parse(Encoding.UTF8.GetString(requestData, SizeOfInt32InBytes * 3 + remoteIpAddressLen, remotePortNumberLen));

            var remoteFolderPathLen = BitConverter.ToInt32(requestData, (SizeOfInt32InBytes * 3) + remoteIpAddressLen + remotePortNumberLen);
            var remoteFolder = Encoding.UTF8.GetString(requestData, (SizeOfInt32InBytes * 4) + remoteIpAddressLen + remotePortNumberLen, remoteFolderPathLen);

            return (remoteIp, remotePort, remoteFolder);
        }

        public static (
            string requestorIpAddress,
            int requestortPort,
            string targetFolder,
            FileInfoList fileInfo)
            ReadFileListResponse(byte[] requestData)
        {
            var remoteServerIpLen = BitConverter.ToInt32(requestData, SizeOfInt32InBytes);
            var remoteIp = Encoding.UTF8.GetString(requestData, SizeOfInt32InBytes * 2, remoteServerIpLen);

            var remoteServerPortLen = BitConverter.ToInt32(requestData, SizeOfInt32InBytes * 2 + remoteServerIpLen);
            var remotePort = int.Parse(Encoding.UTF8.GetString(requestData, SizeOfInt32InBytes * 3 + remoteServerIpLen, remoteServerPortLen));

            var targetFolderLen = BitConverter.ToInt32(requestData, SizeOfInt32InBytes * 3 + remoteServerIpLen + remoteServerPortLen);
            var targetFolder = Encoding.UTF8.GetString(requestData, SizeOfInt32InBytes * 4 + remoteServerIpLen + remoteServerPortLen, targetFolderLen);

            var fileInfoLen = BitConverter.ToInt32(requestData, SizeOfInt32InBytes * 4 + remoteServerIpLen + remoteServerPortLen + targetFolderLen);
            var fileInfo = Encoding.UTF8.GetString(requestData, SizeOfInt32InBytes * 5 + remoteServerIpLen + remoteServerPortLen + targetFolderLen, fileInfoLen);

            var fileInfoSeparatorLen = BitConverter.ToInt32(requestData, SizeOfInt32InBytes * 5 + remoteServerIpLen + remoteServerPortLen + targetFolderLen + fileInfoLen);
            var fileInfoSeparator = Encoding.UTF8.GetString(requestData, SizeOfInt32InBytes * 6 + remoteServerIpLen + remoteServerPortLen + targetFolderLen + fileInfoLen, fileInfoSeparatorLen);

            var fileSeparatorLen = BitConverter.ToInt32(requestData, SizeOfInt32InBytes * 6 + remoteServerIpLen + remoteServerPortLen + targetFolderLen + fileInfoLen + fileInfoSeparatorLen);
            var fileSeparator = Encoding.UTF8.GetString(requestData, SizeOfInt32InBytes * 7 + remoteServerIpLen + remoteServerPortLen + targetFolderLen + fileInfoLen + fileInfoSeparatorLen, fileSeparatorLen);

            var fileInfoList = new FileInfoList();
            foreach (var infoString in fileInfo.Split(fileSeparator))
            {
                var infoSplit = infoString.Split(fileInfoSeparator);
                if (infoSplit.Length == 2)
                {
                    var filePath = infoSplit[0];
                    if (!long.TryParse(infoSplit[1], out var fileSizeBytes)) continue;

                    (string filePath, long fileSizeBytes) fi = (filePath: filePath, fileSizeBytes: fileSizeBytes);
                    fileInfoList.Add(fi);
                }
            }

            return (remoteIp, remotePort, targetFolder, fileInfoList);
        }

        public static (string remoteIpAddress, int remotePortNumber)
            ReadServerConnectionInfo(byte[] requestData)
        {
            var remoteIpAddressLen = BitConverter.ToInt32(requestData, SizeOfInt32InBytes);
            var remoteIp = Encoding.UTF8.GetString(requestData, SizeOfInt32InBytes * 2, remoteIpAddressLen);

            var remotePortNumberLen = BitConverter.ToInt32(requestData, SizeOfInt32InBytes * 2 + remoteIpAddressLen);
            var remotePort = int.Parse(Encoding.UTF8.GetString(requestData, SizeOfInt32InBytes * 3 + remoteIpAddressLen, remotePortNumberLen));

            return (remoteIp, remotePort);
        }

        public static (string remoteIpAddress, int remotePortNumber, int remoteServerTransferId, long responseCode)
            ReadRetryFileTransferRequest(byte[] requestData)
        {
            var remoteIpAddressLen = BitConverter.ToInt32(requestData, SizeOfInt32InBytes);
            var remoteIp = Encoding.UTF8.GetString(requestData, SizeOfInt32InBytes * 2, remoteIpAddressLen);
            var remotePort = BitConverter.ToInt32(requestData, SizeOfInt32InBytes * 3 + remoteIpAddressLen);
            var remoteServerTransferId = BitConverter.ToInt32(requestData, SizeOfInt32InBytes * 5 + remoteIpAddressLen);
            var responseCode = BitConverter.ToInt64(requestData, SizeOfInt32InBytes * 7 + remoteIpAddressLen);

            return (remoteIp, remotePort, remoteServerTransferId, responseCode);
        }

        public static (
            string remoteIpAddress,
            int remotePortNumber,
            int remoteServerTransferId,
            int retryLimit,
            long lockoutExpireTimeTicks)
            ReadRetryLimitExceededRequest(byte[] requestData)
        {
            var remoteIpAddressLen = BitConverter.ToInt32(requestData, SizeOfInt32InBytes);
            var remoteIp = Encoding.UTF8.GetString(requestData, SizeOfInt32InBytes * 2, remoteIpAddressLen);
            var remotePort = BitConverter.ToInt32(requestData, SizeOfInt32InBytes * 3 + remoteIpAddressLen);
            var remoteServerTransferId = BitConverter.ToInt32(requestData, SizeOfInt32InBytes * 5 + remoteIpAddressLen);
            var retryLimit = BitConverter.ToInt32(requestData, SizeOfInt32InBytes * 7 + remoteIpAddressLen);
            var lockoutExpireTimeTicks = BitConverter.ToInt64(requestData, SizeOfInt32InBytes * 9 + remoteIpAddressLen);

            return (remoteIp, remotePort, remoteServerTransferId, retryLimit, lockoutExpireTimeTicks);
        }

        public static (string remoteIpAddress, int remotePortNumber, long responseCode)
            ReadRequestWithInt64Value(byte[] requestData)
        {
            var remoteIpAddressLen = BitConverter.ToInt32(requestData, SizeOfInt32InBytes);
            var remoteIp = Encoding.UTF8.GetString(requestData, SizeOfInt32InBytes * 2, remoteIpAddressLen);            
            var remotePort = BitConverter.ToInt32(requestData, SizeOfInt32InBytes * 3 + remoteIpAddressLen);
            var responseCode = BitConverter.ToInt64(requestData, SizeOfInt32InBytes * 5 + remoteIpAddressLen);

            return (remoteIp, remotePort, responseCode);
        }

        public static (string remoteIpAddress, int remotePortNumber, string message)
            ReadRequestWithStringValue(byte[] requestData)
        {
            var remoteIpAddressLen = BitConverter.ToInt32(requestData, SizeOfInt32InBytes);
            var remoteIp = Encoding.UTF8.GetString(requestData, SizeOfInt32InBytes * 2, remoteIpAddressLen);

            var remotePortNumberLen = BitConverter.ToInt32(requestData, SizeOfInt32InBytes * 2 + remoteIpAddressLen);
            var remotePort = int.Parse(Encoding.UTF8.GetString(requestData, SizeOfInt32InBytes * 3 + remoteIpAddressLen, remotePortNumberLen));

            var messageLen = BitConverter.ToInt32(requestData, SizeOfInt32InBytes * 3 + remoteIpAddressLen + remotePortNumberLen);
            var message = Encoding.UTF8.GetString(requestData, SizeOfInt32InBytes * 4 + remoteIpAddressLen + remotePortNumberLen, messageLen);

            return (remoteIp, remotePort, message);
        }

        public static (string remoteIpAddress, int remotePortNumber, string remoteFolderPath)
            ReadTransferFolderResponse(byte[] requestData)
        {
            var remoteIpAddressLen = BitConverter.ToInt32(requestData, SizeOfInt32InBytes);
            var remoteIp = Encoding.UTF8.GetString(requestData, SizeOfInt32InBytes * 2, remoteIpAddressLen);

            var remotePortNumberLen = BitConverter.ToInt32(requestData, SizeOfInt32InBytes * 2 + remoteIpAddressLen);
            var remotePort = int.Parse(Encoding.UTF8.GetString(requestData, SizeOfInt32InBytes * 3 + remoteIpAddressLen, remotePortNumberLen));

            var remoteFolderPathLen = BitConverter.ToInt32(requestData, SizeOfInt32InBytes * 3 + remoteIpAddressLen + remotePortNumberLen);
            var remoteFolder = Encoding.UTF8.GetString(requestData, SizeOfInt32InBytes * 4 + remoteIpAddressLen + remotePortNumberLen, remoteFolderPathLen);

            return (remoteIp, remotePort, remoteFolder);
        }

        public static (string remoteIpAddress, int remotePortNumber, string publicIpAddress)
            ReadPublicIpAddressResponse(byte[] requestData)
        {
            var remoteIpAddressLen = BitConverter.ToInt32(requestData, SizeOfInt32InBytes);
            var remoteIp = Encoding.UTF8.GetString(requestData, SizeOfInt32InBytes * 2, remoteIpAddressLen);

            var remotePortNumberLen = BitConverter.ToInt32(requestData, SizeOfInt32InBytes * 2 + remoteIpAddressLen);
            var remotePort = int.Parse(Encoding.UTF8.GetString(requestData, SizeOfInt32InBytes * 3 + remoteIpAddressLen, remotePortNumberLen));

            var publicIpLen = BitConverter.ToInt32(requestData, SizeOfInt32InBytes * 3 + remoteIpAddressLen + remotePortNumberLen);
            var publicIp = Encoding.UTF8.GetString(requestData, SizeOfInt32InBytes * 4 + remoteIpAddressLen + remotePortNumberLen, publicIpLen);

            return (remoteIp, remotePort, publicIp);
        }

        public static
            (string remoteIpAddress,
            int remotePortNumber,
            string publicIpAddress,
            string remoteFolderPath) ReadServerInfoResponse(byte[] requestData)
        {
            var remoteIpAddressLen = BitConverter.ToInt32(requestData, SizeOfInt32InBytes);
            var remoteIp = Encoding.UTF8.GetString(requestData, SizeOfInt32InBytes * 2, remoteIpAddressLen);

            var remotePortNumberLen = BitConverter.ToInt32(requestData, SizeOfInt32InBytes * 2 + remoteIpAddressLen);
            var remotePort = int.Parse(Encoding.UTF8.GetString(requestData, SizeOfInt32InBytes * 3 + remoteIpAddressLen, remotePortNumberLen));

            var publicIpLen = BitConverter.ToInt32(requestData, SizeOfInt32InBytes * 3 + remoteIpAddressLen + remotePortNumberLen);
            var publicIp = Encoding.UTF8.GetString(requestData, SizeOfInt32InBytes * 4 + remoteIpAddressLen + remotePortNumberLen, publicIpLen);

            var remoteFolderPathLen = BitConverter.ToInt32(requestData, SizeOfInt32InBytes * 4 + remoteIpAddressLen + remotePortNumberLen + publicIpLen);
            var remoteFolder = Encoding.UTF8.GetString(requestData, SizeOfInt32InBytes * 5 + remoteIpAddressLen + remotePortNumberLen + publicIpLen, remoteFolderPathLen);

            return (remoteIp, remotePort, publicIp, remoteFolder);
        }
    }
}
