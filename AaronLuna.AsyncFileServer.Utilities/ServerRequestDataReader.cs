namespace AaronLuna.AsyncFileServer.Utilities
{
    using System;
    using System.IO;
    using System.Net;
    using System.Text;

    using Model;
    using Common;
    using Common.Network;

    public static class ServerRequestDataReader
    {
        public static (IPAddress remoteServerIpAddress,
            int remoteServerPortNumber,
            string textMessage,
            int fileTransferId,
            IPAddress remoteServerLocalIpAddress,
            IPAddress remoteServerPublicIpAddress,
            string requestedFilePath,
            string localFilePath,
            string localFolderPath,
            string remoteFolderPath,
            FileInfoList fileInfoList,
            long fileSizeBytes,
            long fileTransferResponseCode,
            long lockoutExpireTimeTicks,
            int fileTransferRetryCounter,
            int fileTransferRetryLimit) ReadDataForRequestType(ServerRequestType requestType, byte[] requestData)
        {
            var remoteServerIpString = string.Empty;
            var remoteServerPortNumber = 0;
            var textMessage = string.Empty;
            var fileTransferId = 0;
            var remoteServerLocalIpString = string.Empty;
            var remoteServerPublicIpString = string.Empty;
            var requestedFilePath = string.Empty;
            var localFilePath = string.Empty;
            var localFolderPath = string.Empty;
            var remoteFolderPath = string.Empty;
            var fileInfoList = new FileInfoList();
            long fileSizeBytes = 0;
            long fileTransferResponseCode = 0;
            long lockoutExpireTimeTicks = 0;
            long fileTransferIdInt64 = 0;
            var fileTransferRetryCounter = 0;
            var fileTransferRetryLimit = 0;

            switch (requestType)
            {
                case ServerRequestType.None:
                    break;

                case ServerRequestType.ServerInfoResponse:

                    (remoteServerLocalIpString,
                        remoteServerPortNumber,
                        remoteServerPublicIpString,
                        remoteFolderPath) = ReadServerInfoResponse(requestData);
                    
                    break;

                case ServerRequestType.TextMessage:

                    (remoteServerIpString,
                        remoteServerPortNumber,
                        textMessage) = ReadRequestWithStringValue(requestData);

                    break;

                case ServerRequestType.InboundFileTransferRequest:

                    (fileTransferResponseCode,
                        fileTransferId,
                        fileTransferRetryCounter,
                        fileTransferRetryLimit,
                        localFilePath,
                        fileSizeBytes,
                        remoteServerIpString,
                        remoteServerPortNumber) = ReadInboundFileTransferRequest(requestData);

                    break;

                case ServerRequestType.OutboundFileTransferRequest:

                    (fileTransferId,
                        requestedFilePath,
                        remoteServerIpString,
                        remoteServerPortNumber,
                        remoteFolderPath) = ReadOutboundFileTransferRequest(requestData);

                    break;

                case ServerRequestType.FileListRequest:

                    (remoteServerIpString,
                        remoteServerPortNumber,
                        localFolderPath) = ReadRequestWithStringValue(requestData);

                    break;

                case ServerRequestType.FileListResponse:

                    (remoteServerIpString,
                        remoteServerPortNumber,
                        remoteFolderPath,
                        fileInfoList) = ReadFileListResponse(requestData);

                    break;

                case ServerRequestType.ServerInfoRequest:
                case ServerRequestType.NoFilesAvailableForDownload:
                case ServerRequestType.RequestedFolderDoesNotExist:
                case ServerRequestType.ShutdownServerCommand:

                    (remoteServerIpString,
                        remoteServerPortNumber) = ReadServerConnectionInfo(requestData);

                    break;

                case ServerRequestType.FileTransferAccepted:
                case ServerRequestType.FileTransferRejected:
                case ServerRequestType.FileTransferStalled:
                case ServerRequestType.FileTransferComplete:
                case ServerRequestType.RetryOutboundFileTransfer:

                    (remoteServerIpString,
                        remoteServerPortNumber,
                        fileTransferResponseCode) = ReadRequestWithInt64Value(requestData);

                    break;

                case ServerRequestType.RequestedFileDoesNotExist:

                    (remoteServerIpString,
                        remoteServerPortNumber,
                        fileTransferIdInt64) = ReadRequestWithInt64Value(requestData);

                    fileTransferId = Convert.ToInt32(fileTransferIdInt64);

                    break;

                case ServerRequestType.RetryLimitExceeded:

                    (remoteServerIpString,
                        remoteServerPortNumber,
                        fileTransferId,
                        fileTransferRetryLimit,
                        lockoutExpireTimeTicks) = ReadRetryLimitExceededRequest(requestData);

                    break;

                case ServerRequestType.RetryLockoutExpired:
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }

            var remoteServerIpAddress = string.IsNullOrEmpty(remoteServerIpString)
                ? IPAddress.None
                : NetworkUtilities.ParseSingleIPv4Address(remoteServerIpString).Value;

            var remoteServerLocalIpAddress = string.IsNullOrEmpty(remoteServerLocalIpString)
                ? IPAddress.None
                : NetworkUtilities.ParseSingleIPv4Address(remoteServerLocalIpString).Value;

            var remoteServerPublicIpAddress = string.IsNullOrEmpty(remoteServerPublicIpString)
                ? IPAddress.None
                : NetworkUtilities.ParseSingleIPv4Address(remoteServerPublicIpString).Value;

            return (remoteServerIpAddress,
                remoteServerPortNumber,
                textMessage,
                fileTransferId,
                remoteServerLocalIpAddress,
                remoteServerPublicIpAddress,
                requestedFilePath,
                localFilePath,
                localFolderPath,
                remoteFolderPath,
                fileInfoList,
                fileSizeBytes,
                fileTransferResponseCode,
                lockoutExpireTimeTicks,
                fileTransferRetryCounter,
                fileTransferRetryLimit);
        }
        
        public static (
            string message,
            string remoteIpAddress,
            int remotePortNumber) ReadTextMessage(byte[] requestData)
        {
            var messageLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes);
            var message = Encoding.UTF8.GetString(requestData, Constants.SizeOfInt32InBytes * 2, messageLen);

            var clientIpLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 2 + messageLen);
            var clientIp = Encoding.UTF8.GetString(requestData, Constants.SizeOfInt32InBytes * 3 + messageLen, clientIpLen);

            var clientPortLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 3 + messageLen + clientIpLen);
            var clientPort = int.Parse(Encoding.UTF8.GetString(requestData, Constants.SizeOfInt32InBytes * 4 + messageLen + clientIpLen, clientPortLen));

            return (
                message,
                clientIp,
                clientPort);
        }

        public static (
            long responseCode,
            int transferid,
            int retryCounter,
            int retryLimit,
            string filePath,
            long fileSizeInBytes,
            string remoteIpAddress,
            int remotePort) ReadInboundFileTransferRequest(byte[] requestData)
        {
            var responseCodeLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes);
            var responseCode = BitConverter.ToInt64(requestData, Constants.SizeOfInt32InBytes * 2);

            var transferIdLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 2 + responseCodeLen);
            var transferId = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 3 + responseCodeLen);

            var retryCounterLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 3 + responseCodeLen + transferIdLen);
            var retryCounter = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 4 + responseCodeLen + transferIdLen);

            var retryLimitLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 4 + responseCodeLen + transferIdLen + retryCounterLen);
            var retryLimit = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 5 + responseCodeLen + transferIdLen + retryCounterLen);

            var fileNameLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 5 + responseCodeLen + transferIdLen + retryCounterLen + retryLimitLen);
            var fileName = Encoding.UTF8.GetString(requestData, Constants.SizeOfInt32InBytes * 6 + responseCodeLen + transferIdLen + retryCounterLen + retryLimitLen, fileNameLen);

            var fileSizeLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 6 + responseCodeLen + transferIdLen + retryCounterLen + retryLimitLen + fileNameLen);
            var fileSize = BitConverter.ToInt64(requestData, Constants.SizeOfInt32InBytes * 7 + responseCodeLen + transferIdLen + retryCounterLen + retryLimitLen + fileNameLen);

            var remoteIpLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 7 + responseCodeLen + transferIdLen + retryCounterLen + retryLimitLen + fileNameLen + fileSizeLen);
            var remoteIpAddress = Encoding.UTF8.GetString(requestData, Constants.SizeOfInt32InBytes * 8 + responseCodeLen + transferIdLen + retryCounterLen + retryLimitLen + fileNameLen + fileSizeLen, remoteIpLen);

            var remotePortLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 8 + responseCodeLen + transferIdLen + retryCounterLen + retryLimitLen + fileNameLen + fileSizeLen + remoteIpLen);
            var remotePort = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 9 + responseCodeLen + transferIdLen + retryCounterLen + retryLimitLen + fileNameLen + fileSizeLen + remoteIpLen);

            var targetFolderLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 9 + responseCodeLen + transferIdLen + retryCounterLen + retryLimitLen + fileNameLen + fileSizeLen + remoteIpLen + remotePortLen);
            var targerFolder = Encoding.UTF8.GetString(requestData, Constants.SizeOfInt32InBytes * 10 + responseCodeLen + transferIdLen + retryCounterLen + retryLimitLen + fileNameLen + fileSizeLen + remoteIpLen + remotePortLen, targetFolderLen);

            var localFilePath = Path.Combine(targerFolder, fileName);

            return (
                responseCode,
                transferId,
                retryCounter,
                retryLimit,
                localFilePath,
                fileSize,
                remoteIpAddress,
                remotePort);
        }

        public static (
            int remoteServerTransferId,
            string localFilePath,
            string remoteServerIpAddress,
            int remoteServerPortNumber,
            string remoteFolderPath) ReadOutboundFileTransferRequest(byte[] requestData)
        {
            var remoteServerTransferIdLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes);
            var remoteServerTransferId = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 2);

            var localFilePathLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 2 + remoteServerTransferIdLen);
            var localFilePath = Encoding.UTF8.GetString(requestData, Constants.SizeOfInt32InBytes * 3 + remoteServerTransferIdLen, localFilePathLen);

            var remoteServerIpLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 3 + remoteServerTransferIdLen + localFilePathLen);
            var remoteServerIp = Encoding.UTF8.GetString(requestData, Constants.SizeOfInt32InBytes * 4 + remoteServerTransferIdLen + localFilePathLen, remoteServerIpLen);

            var remoteServerPortLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 4 + remoteServerTransferIdLen + localFilePathLen + remoteServerIpLen);
            var remoteServerPort = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 5 + remoteServerTransferIdLen + localFilePathLen + remoteServerIpLen);

            var remoteFolderData = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 5 + remoteServerTransferIdLen + localFilePathLen + remoteServerIpLen + remoteServerPortLen);
            var remoteFolder = Encoding.UTF8.GetString(requestData, Constants.SizeOfInt32InBytes * 6 + remoteServerTransferIdLen + localFilePathLen + remoteServerIpLen + remoteServerPortLen, remoteFolderData);

            return (
                remoteServerTransferId,
                localFilePath,
                remoteServerIp,
                remoteServerPort,
                remoteFolder);
        }
        
        public static (
            string requestorIpAddress,
            int requestortPort,
            string targetFolder,
            FileInfoList fileInfo) ReadFileListResponse(byte[] requestData)
        {
            var remoteServerIpLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes);
            var remoteIp = Encoding.UTF8.GetString(requestData, Constants.SizeOfInt32InBytes * 2, remoteServerIpLen);

            var remoteServerPortLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 2 + remoteServerIpLen);
            var remotePort = int.Parse(Encoding.UTF8.GetString(requestData, Constants.SizeOfInt32InBytes * 3 + remoteServerIpLen, remoteServerPortLen));

            var targetFolderLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 3 + remoteServerIpLen + remoteServerPortLen);
            var targetFolder = Encoding.UTF8.GetString(requestData, Constants.SizeOfInt32InBytes * 4 + remoteServerIpLen + remoteServerPortLen, targetFolderLen);

            var fileInfoLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 4 + remoteServerIpLen + remoteServerPortLen + targetFolderLen);
            var fileInfo = Encoding.UTF8.GetString(requestData, Constants.SizeOfInt32InBytes * 5 + remoteServerIpLen + remoteServerPortLen + targetFolderLen, fileInfoLen);

            var fileInfoSeparatorLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 5 + remoteServerIpLen + remoteServerPortLen + targetFolderLen + fileInfoLen);
            var fileInfoSeparator = Encoding.UTF8.GetString(requestData, Constants.SizeOfInt32InBytes * 6 + remoteServerIpLen + remoteServerPortLen + targetFolderLen + fileInfoLen, fileInfoSeparatorLen);

            var fileSeparatorLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 6 + remoteServerIpLen + remoteServerPortLen + targetFolderLen + fileInfoLen + fileInfoSeparatorLen);
            var fileSeparator = Encoding.UTF8.GetString(requestData, Constants.SizeOfInt32InBytes * 7 + remoteServerIpLen + remoteServerPortLen + targetFolderLen + fileInfoLen + fileInfoSeparatorLen, fileSeparatorLen);

            var fileInfoList = new FileInfoList();
            foreach (var infoString in fileInfo.Split(fileSeparator))
            {
                var infoSplit = infoString.Split(fileInfoSeparator);
                if (infoSplit.Length != 2) continue;

                var filePath = infoSplit[0];
                if (!long.TryParse(infoSplit[1], out var fileSizeBytes)) continue;

                var fi = (filePath: filePath, fileSizeBytes: fileSizeBytes);
                fileInfoList.Add(fi);
            }

            return (
                remoteIp,
                remotePort,
                targetFolder,
                fileInfoList);
        }

        public static (
            string remoteIpAddress,
            int remotePortNumber) ReadServerConnectionInfo(byte[] requestData)
        {
            var remoteIpAddressLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes);
            var remoteIp = Encoding.UTF8.GetString(requestData, Constants.SizeOfInt32InBytes * 2, remoteIpAddressLen);

            var remotePortNumberLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 2 + remoteIpAddressLen);
            var remotePort = int.Parse(Encoding.UTF8.GetString(requestData, Constants.SizeOfInt32InBytes * 3 + remoteIpAddressLen, remotePortNumberLen));

            return (
                remoteIp,
                remotePort);
        }

        public static (
            string remoteIpAddress,
            int remotePortNumber,
            int remoteServerTransferId,
            int retryLimit,
            long lockoutExpireTimeTicks) ReadRetryLimitExceededRequest(byte[] requestData)
        {
            var remoteIpAddressLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes);
            var remoteIp = Encoding.UTF8.GetString(requestData, Constants.SizeOfInt32InBytes * 2, remoteIpAddressLen);
            var remotePort = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 3 + remoteIpAddressLen);
            var remoteServerTransferId = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 5 + remoteIpAddressLen);
            var retryLimit = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 7 + remoteIpAddressLen);
            var lockoutExpireTimeTicks = BitConverter.ToInt64(requestData, Constants.SizeOfInt32InBytes * 9 + remoteIpAddressLen);

            return (
                remoteIp,
                remotePort,
                remoteServerTransferId,
                retryLimit,
                lockoutExpireTimeTicks);
        }

        public static (
            string remoteIpAddress,
            int remotePortNumber,
            long responseCode) ReadRequestWithInt64Value(byte[] requestData)
        {
            var remoteIpAddressLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes);
            var remoteIp = Encoding.UTF8.GetString(requestData, Constants.SizeOfInt32InBytes * 2, remoteIpAddressLen);
            var remotePort = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 3 + remoteIpAddressLen);
            var responseCode = BitConverter.ToInt64(requestData, Constants.SizeOfInt32InBytes * 5 + remoteIpAddressLen);

            return (
                remoteIp,
                remotePort,
                responseCode);
        }

        public static (
            string remoteIpAddress,
            int remotePortNumber,
            string message) ReadRequestWithStringValue(byte[] requestData)
        {
            var remoteIpAddressLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes);
            var remoteIp = Encoding.UTF8.GetString(requestData, Constants.SizeOfInt32InBytes * 2, remoteIpAddressLen);

            var remotePortNumberLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 2 + remoteIpAddressLen);
            var remotePort = int.Parse(Encoding.UTF8.GetString(requestData, Constants.SizeOfInt32InBytes * 3 + remoteIpAddressLen, remotePortNumberLen));

            var messageLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 3 + remoteIpAddressLen + remotePortNumberLen);
            var message = Encoding.UTF8.GetString(requestData, Constants.SizeOfInt32InBytes * 4 + remoteIpAddressLen + remotePortNumberLen, messageLen);

            return (
                remoteIp,
                remotePort,
                message);
        }

        public static (
            string remoteIpAddress,
            int remotePortNumber,
            string remoteFolderPath) ReadTransferFolderResponse(byte[] requestData)
        {
            var remoteIpAddressLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes);
            var remoteIp = Encoding.UTF8.GetString(requestData, Constants.SizeOfInt32InBytes * 2, remoteIpAddressLen);

            var remotePortNumberLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 2 + remoteIpAddressLen);
            var remotePort = int.Parse(Encoding.UTF8.GetString(requestData, Constants.SizeOfInt32InBytes * 3 + remoteIpAddressLen, remotePortNumberLen));

            var remoteFolderPathLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 3 + remoteIpAddressLen + remotePortNumberLen);
            var remoteFolder = Encoding.UTF8.GetString(requestData, Constants.SizeOfInt32InBytes * 4 + remoteIpAddressLen + remotePortNumberLen, remoteFolderPathLen);

            return (
                remoteIp,
                remotePort,
                remoteFolder);
        }

        public static (
            string remoteIpAddress,
            int remotePortNumber,
            string publicIpAddress) ReadPublicIpAddressResponse(byte[] requestData)
        {
            var remoteIpAddressLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes);
            var remoteIp = Encoding.UTF8.GetString(requestData, Constants.SizeOfInt32InBytes * 2, remoteIpAddressLen);

            var remotePortNumberLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 2 + remoteIpAddressLen);
            var remotePort = int.Parse(Encoding.UTF8.GetString(requestData, Constants.SizeOfInt32InBytes * 3 + remoteIpAddressLen, remotePortNumberLen));

            var publicIpLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 3 + remoteIpAddressLen + remotePortNumberLen);
            var publicIp = Encoding.UTF8.GetString(requestData, Constants.SizeOfInt32InBytes * 4 + remoteIpAddressLen + remotePortNumberLen, publicIpLen);

            return (
                remoteIp,
                remotePort,
                publicIp);
        }

        public static (
            string remoteIpAddress,
            int remotePortNumber,
            string publicIpAddress,
            string remoteFolderPath) ReadServerInfoResponse(byte[] requestData)
        {
            var remoteIpAddressLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes);
            var remoteIp = Encoding.UTF8.GetString(requestData, Constants.SizeOfInt32InBytes * 2, remoteIpAddressLen);

            var remotePortNumberLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 2 + remoteIpAddressLen);
            var remotePort = int.Parse(Encoding.UTF8.GetString(requestData, Constants.SizeOfInt32InBytes * 3 + remoteIpAddressLen, remotePortNumberLen));

            var publicIpLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 3 + remoteIpAddressLen + remotePortNumberLen);
            var publicIp = Encoding.UTF8.GetString(requestData, Constants.SizeOfInt32InBytes * 4 + remoteIpAddressLen + remotePortNumberLen, publicIpLen);

            var remoteFolderPathLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 4 + remoteIpAddressLen + remotePortNumberLen + publicIpLen);
            var remoteFolder = Encoding.UTF8.GetString(requestData, Constants.SizeOfInt32InBytes * 5 + remoteIpAddressLen + remotePortNumberLen + publicIpLen, remoteFolderPathLen);

            return (
                remoteIp,
                remotePort,
                publicIp,
                remoteFolder);
        }
    }
}
