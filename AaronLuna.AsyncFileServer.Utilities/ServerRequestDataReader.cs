namespace AaronLuna.AsyncFileServer.Utilities
{
    using System;
    using System.Net;
    using System.Text;

    using Model;
    using Common;
    using Common.Network;

    public static class ServerRequestDataReader
    {
        public static ServerRequestType ReadRequestType(byte[] requestBytes)
        {
            var requestTypeData = BitConverter.ToInt32(requestBytes, 0).ToString();
            return (ServerRequestType)Enum.Parse(typeof(ServerRequestType), requestTypeData);
        }

        public static ServerInfo ReadRemoteServerInfo(byte[] requestBytes)
        {
            var (remoteServerIpAddress,
                remoteServerLocalIpAddress,
                remoteServerPublicIpAddress,
                remoteServerPortNumber,
                platform,
                _,
                _,
                _,
                _,
                _,
                remoteFolderPath,
                _,
                _,
                _,
                _,
                _) = ReadRequestBytes(requestBytes);

            if (ReadRequestType(requestBytes) == ServerRequestType.ServerInfoResponse)
            {
                return new ServerInfo
                {
                    LocalIpAddress = remoteServerLocalIpAddress,
                    PublicIpAddress = remoteServerPublicIpAddress,
                    Platform = platform,
                    PortNumber = remoteServerPortNumber,
                    TransferFolder = remoteFolderPath
                };
            }

            return new ServerInfo
            {
                SessionIpAddress = remoteServerIpAddress,
                PortNumber = remoteServerPortNumber,

                TransferFolder = string.IsNullOrEmpty(remoteFolderPath)
                    ? string.Empty
                    : remoteFolderPath
            };
        }

        public static (
            IPAddress remoteServerSessionIpAddress,     // 1
            IPAddress remoteServerLocalIpAddress,       // 2
            IPAddress remoteServerPublicIpAddress,      // 3
            int remoteServerPortNumber,                 // 4
            ServerPlatform remoteServerPlatform,                    // 5
            string textMessage,                         // 6
            FileInfoList fileInfoList,                  // 7
            string fileName,                            // 8
            long fileSizeInBytes,                         // 9
            string localFolderPath,                     // 10
            string remoteFolderPath,                    // 11
            long fileTransferResponseCode,              // 12
            int remoteServerTransferId,                 // 13
            int fileTransferRetryCounter,               // 14
            int fileTransferRetryLimit,                 // 15
            long lockoutExpireTimeInTicks)                // 16
            ReadRequestBytes(byte[] requestBytes)
        {
            var remoteServerIpString = string.Empty;
            var remoteServerLocalIpString = string.Empty;
            var remoteServerPublicIpString = string.Empty;
            var remoteServerPortNumber = 0;
            var remoteServerPlatform = ServerPlatform.None;
            var textMessage = string.Empty;
            var fileInfoList = new FileInfoList();
            var fileName = string.Empty;
            long fileSizeInBytes = 0;
            var localFolderPath = string.Empty;
            var remoteFolderPath = string.Empty;
            long fileTransferResponseCode = 0;
            var remoteServerTransferId = 0;
            var fileTransferRetryCounter = 0;
            var fileTransferRetryLimit = 0;
            long lockoutExpireTimeInTicks = 0;

            switch (ReadRequestType(requestBytes))
            {
                case ServerRequestType.None:
                    break;

                case ServerRequestType.ServerInfoResponse:

                    (remoteServerLocalIpString,
                        remoteServerPortNumber,
                        remoteServerPlatform,
                        remoteServerPublicIpString,
                        remoteFolderPath) = ReadServerInfoResponse(requestBytes);

                    break;

                case ServerRequestType.TextMessage:

                    (remoteServerIpString,
                        remoteServerPortNumber,
                        textMessage) = ReadRequestWithStringValue(requestBytes);

                    break;

                case ServerRequestType.InboundFileTransferRequest:

                    (fileTransferResponseCode,
                        remoteServerTransferId,
                        fileTransferRetryCounter,
                        fileTransferRetryLimit,
                        fileName,
                        fileSizeInBytes,
                        remoteFolderPath,
                        localFolderPath,                        
                        remoteServerIpString,
                        remoteServerPortNumber) = ReadInboundFileTransferRequest(requestBytes);

                    break;

                case ServerRequestType.OutboundFileTransferRequest:

                    (remoteServerTransferId,
                        fileName,
                        localFolderPath,
                        remoteServerIpString,
                        remoteServerPortNumber,
                        remoteFolderPath) = ReadOutboundFileTransferRequest(requestBytes);

                    break;

                case ServerRequestType.FileListRequest:

                    (remoteServerIpString,
                        remoteServerPortNumber,
                        localFolderPath) = ReadRequestWithStringValue(requestBytes);

                    break;

                case ServerRequestType.FileListResponse:

                    (remoteServerIpString,
                        remoteServerPortNumber,
                        remoteFolderPath,
                        fileInfoList) = ReadFileListResponse(requestBytes);

                    break;

                case ServerRequestType.ServerInfoRequest:
                case ServerRequestType.NoFilesAvailableForDownload:
                case ServerRequestType.RequestedFolderDoesNotExist:
                case ServerRequestType.ShutdownServerCommand:

                    (remoteServerIpString,
                        remoteServerPortNumber) = ReadServerInfo(requestBytes);

                    break;

                case ServerRequestType.FileTransferAccepted:
                case ServerRequestType.FileTransferRejected:
                case ServerRequestType.FileTransferStalled:
                case ServerRequestType.FileTransferComplete:
                case ServerRequestType.RetryOutboundFileTransfer:

                    (remoteServerIpString,
                        remoteServerPortNumber,
                        fileTransferResponseCode) = ReadRequestWithInt64Value(requestBytes);

                    break;

                case ServerRequestType.RequestedFileDoesNotExist:

                    long fileTransferIdInt64;

                    (remoteServerIpString,
                        remoteServerPortNumber,
                        fileTransferIdInt64) = ReadRequestWithInt64Value(requestBytes);

                    remoteServerTransferId = Convert.ToInt32(fileTransferIdInt64);

                    break;

                case ServerRequestType.RetryLimitExceeded:

                    (remoteServerIpString,
                        remoteServerPortNumber,
                        remoteServerTransferId,
                        fileTransferRetryLimit,
                        lockoutExpireTimeInTicks) = ReadRetryLimitExceededRequest(requestBytes);

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
                remoteServerLocalIpAddress,
                remoteServerPublicIpAddress,
                remoteServerPortNumber,
                remoteServerPlatform,
                textMessage,
                fileInfoList,
                fileName,
                fileSizeInBytes,
                localFolderPath,
                remoteFolderPath,
                fileTransferResponseCode,
                remoteServerTransferId,
                fileTransferRetryCounter,
                fileTransferRetryLimit,
                lockoutExpireTimeInTicks);
        }

        static (
            long responseCode,
            int transferid,
            int retryCounter,
            int retryLimit,
            string fileName,
            long fileSizeInBytes,
            string remoteFolderPath,
            string localFolderPath,
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

            var remoteFolderLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 6 + responseCodeLen + transferIdLen + retryCounterLen + retryLimitLen + fileNameLen);
            var remoteFolder = Encoding.UTF8.GetString(requestData, Constants.SizeOfInt32InBytes * 7 + responseCodeLen + transferIdLen + retryCounterLen + retryLimitLen + fileNameLen, remoteFolderLen);

            var fileSizeLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 7 + responseCodeLen + transferIdLen + retryCounterLen + retryLimitLen + fileNameLen + remoteFolderLen);
            var fileSize = BitConverter.ToInt64(requestData, Constants.SizeOfInt32InBytes * 8 + responseCodeLen + transferIdLen + retryCounterLen + retryLimitLen + fileNameLen + remoteFolderLen);

            var remoteIpLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 8 + responseCodeLen + transferIdLen + retryCounterLen + retryLimitLen + fileNameLen + fileSizeLen + remoteFolderLen);
            var remoteIpAddress = Encoding.UTF8.GetString(requestData, Constants.SizeOfInt32InBytes * 9 + responseCodeLen + transferIdLen + retryCounterLen + retryLimitLen + fileNameLen + fileSizeLen + remoteFolderLen, remoteIpLen);

            var remotePortLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 9 + responseCodeLen + transferIdLen + retryCounterLen + retryLimitLen + fileNameLen + fileSizeLen + remoteFolderLen + remoteIpLen);
            var remotePort = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 10 + responseCodeLen + transferIdLen + retryCounterLen + retryLimitLen + fileNameLen + fileSizeLen + remoteFolderLen + remoteIpLen);

            var localFolderLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 10 + responseCodeLen + transferIdLen + retryCounterLen + retryLimitLen + fileNameLen + fileSizeLen + remoteFolderLen + remoteIpLen + remotePortLen);
            var localFolder = Encoding.UTF8.GetString(requestData, Constants.SizeOfInt32InBytes * 11 + responseCodeLen + transferIdLen + retryCounterLen + retryLimitLen + fileNameLen + fileSizeLen + remoteFolderLen + remoteIpLen + remotePortLen, localFolderLen);
            
            return (
                responseCode,
                transferId,
                retryCounter,
                retryLimit,
                fileName,
                fileSize,
                remoteFolder,
                localFolder,
                remoteIpAddress,
                remotePort);
        }

        static (
            int remoteServerTransferId,
            string fileName,
            string localFolderPath,
            string remoteServerIpAddress,
            int remoteServerPortNumber,
            string remoteFolderPath) ReadOutboundFileTransferRequest(byte[] requestData)
        {
            var remoteServerTransferIdLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes);
            var remoteServerTransferId = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 2);

            var fileNameLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 2 + remoteServerTransferIdLen);
            var fileName = Encoding.UTF8.GetString(requestData, Constants.SizeOfInt32InBytes * 3 + remoteServerTransferIdLen, fileNameLen);

            var localFolderLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 3 + remoteServerTransferIdLen + fileNameLen);
            var localFolder = Encoding.UTF8.GetString(requestData, Constants.SizeOfInt32InBytes * 4 + remoteServerTransferIdLen + fileNameLen, localFolderLen);

            var remoteServerIpLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 4 + remoteServerTransferIdLen + fileNameLen + localFolderLen);
            var remoteServerIp = Encoding.UTF8.GetString(requestData, Constants.SizeOfInt32InBytes * 5 + remoteServerTransferIdLen + fileNameLen + localFolderLen, remoteServerIpLen);

            var remoteServerPortLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 5 + remoteServerTransferIdLen + fileNameLen + localFolderLen + remoteServerIpLen);
            var remoteServerPort = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 6 + remoteServerTransferIdLen + fileNameLen + localFolderLen + remoteServerIpLen);

            var remoteFolderLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 6 + remoteServerTransferIdLen + fileNameLen + localFolderLen + remoteServerIpLen + remoteServerPortLen);
            var remoteFolder = Encoding.UTF8.GetString(requestData, Constants.SizeOfInt32InBytes * 7 + remoteServerTransferIdLen + fileNameLen + localFolderLen + remoteServerIpLen + remoteServerPortLen, remoteFolderLen);

            return (
                remoteServerTransferId,
                fileName,
                localFolder,
                remoteServerIp,
                remoteServerPort,
                remoteFolder);
        }

        static (
            string requestorIpAddress,
            int requestorPort,
            string targetFolder,
            FileInfoList fileInfo) ReadFileListResponse(byte[] requestData)
        {
            var remoteServerIpLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes);
            var remoteIp = Encoding.UTF8.GetString(requestData, Constants.SizeOfInt32InBytes * 2, remoteServerIpLen);

            var remotePort = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 3 + remoteServerIpLen);

            var targetFolderLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 4 + remoteServerIpLen);
            var targetFolder = Encoding.UTF8.GetString(requestData, Constants.SizeOfInt32InBytes * 5 + remoteServerIpLen, targetFolderLen);

            var fileInfoLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 5 + remoteServerIpLen + targetFolderLen);
            var fileInfo = Encoding.UTF8.GetString(requestData, Constants.SizeOfInt32InBytes * 6 + remoteServerIpLen + targetFolderLen, fileInfoLen);

            var fileInfoList = new FileInfoList();
            foreach (var infoString in fileInfo.Split(FileInfoList.FileSeparator))
            {
                var infoSplit = infoString.Split(FileInfoList.FileInfoSeparator);
                if (infoSplit.Length != 3) continue;

                var fileName = infoSplit[0];
                var folderPath = infoSplit[1];
                if (!long.TryParse(infoSplit[2], out var fileSizeBytes)) continue;

                var fi = (fileName: fileName, folderPath: folderPath, fileSizeBytes: fileSizeBytes);
                fileInfoList.Add(fi);
            }

            return (
                remoteIp,
                remotePort,
                targetFolder,
                fileInfoList);
        }

        static (
            string remoteIpAddress,
            int remotePortNumber) ReadServerInfo(byte[] requestData)
        {
            var remoteIpAddressLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes);
            var remoteIp = Encoding.UTF8.GetString(requestData, Constants.SizeOfInt32InBytes * 2, remoteIpAddressLen);

            var remotePort = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 3 + remoteIpAddressLen);

            return (
                remoteIp,
                remotePort);
        }

        static (
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

        static (
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

        static (
            string remoteIpAddress,
            int remotePortNumber,
            string message) ReadRequestWithStringValue(byte[] requestData)
        {
            var remoteIpAddressLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes);
            var remoteIp = Encoding.UTF8.GetString(requestData, Constants.SizeOfInt32InBytes * 2, remoteIpAddressLen);

            var remotePort = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 3 + remoteIpAddressLen);

            var messageLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 4 + remoteIpAddressLen);
            var message = Encoding.UTF8.GetString(requestData, Constants.SizeOfInt32InBytes * 5 + remoteIpAddressLen, messageLen);

            return (
                remoteIp,
                remotePort,
                message);
        }

        static (
            string remoteIpAddress,
            int remotePortNumber,
            ServerPlatform platform,
            string publicIpAddress,
            string remoteFolderPath) ReadServerInfoResponse(byte[] requestData)
        {
            var localIpLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes);
            var localIp = Encoding.UTF8.GetString(requestData, Constants.SizeOfInt32InBytes * 2, localIpLen);

            var portNumber = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 3 + localIpLen);

            var platformData = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 5 + localIpLen).ToString();
            var platform = (ServerPlatform)Enum.Parse(typeof(ServerPlatform), platformData);

            var publicIpLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 6 + localIpLen);
            var publicIp = Encoding.UTF8.GetString(requestData, Constants.SizeOfInt32InBytes * 7 + localIpLen, publicIpLen);

            var transferFolderPathLen = BitConverter.ToInt32(requestData, Constants.SizeOfInt32InBytes * 7 + localIpLen + publicIpLen);
            var transferFolderPath = Encoding.UTF8.GetString(requestData, Constants.SizeOfInt32InBytes * 8 + localIpLen + publicIpLen, transferFolderPathLen);

            return (
                localIp,
                portNumber,
                platform,
                publicIp,
                transferFolderPath);
        }
    }
}
