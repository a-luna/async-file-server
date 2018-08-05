namespace AaronLuna.AsyncFileServer.Utilities
{
    using System;
    using System.Net;
    using System.Text;

    using Model;
    using Common;
    using Common.Network;

    public static class RequestDataReader
    {
        public static RequestType ReadRequestType(byte[] requestBytes)
        {
            return (RequestType)Enum.Parse(typeof(RequestType), requestBytes[0].ToString());
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

            if (ReadRequestType(requestBytes) == RequestType.ServerInfoResponse)
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
            ServerPlatform remoteServerPlatform,        // 5
            string textMessage,                         // 6
            FileInfoList fileInfoList,                  // 7
            string fileName,                            // 8
            long fileSizeInBytes,                       // 9
            string localFolderPath,                     // 10
            string remoteFolderPath,                    // 11
            long fileTransferResponseCode,              // 12
            int remoteServerTransferId,                 // 13
            int fileTransferRetryCounter,               // 14
            int fileTransferRetryLimit,                 // 15
            long lockoutExpireTimeInTicks)              // 16
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
                case RequestType.None:
                    break;

                case RequestType.ServerInfoResponse:

                    (remoteServerLocalIpString,
                        remoteServerPortNumber,
                        remoteServerPlatform,
                        remoteServerPublicIpString,
                        remoteFolderPath) = ReadServerInfoResponse(requestBytes);

                    break;

                case RequestType.TextMessage:

                    (remoteServerIpString,
                        remoteServerPortNumber,
                        textMessage) = ReadRequestWithStringValue(requestBytes);

                    break;

                case RequestType.InboundFileTransferRequest:

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

                case RequestType.OutboundFileTransferRequest:

                    (remoteServerTransferId,
                        fileName,
                        localFolderPath,
                        remoteServerIpString,
                        remoteServerPortNumber,
                        remoteFolderPath) = ReadOutboundFileTransferRequest(requestBytes);

                    break;

                case RequestType.FileListRequest:

                    (remoteServerIpString,
                        remoteServerPortNumber,
                        localFolderPath) = ReadRequestWithStringValue(requestBytes);

                    break;

                case RequestType.FileListResponse:

                    (remoteServerIpString,
                        remoteServerPortNumber,
                        remoteFolderPath,
                        fileInfoList) = ReadFileListResponse(requestBytes);

                    break;

                case RequestType.ServerInfoRequest:
                case RequestType.NoFilesAvailableForDownload:
                case RequestType.RequestedFolderDoesNotExist:
                case RequestType.ShutdownServerCommand:

                    (remoteServerIpString,
                        remoteServerPortNumber) = ReadServerInfo(requestBytes);

                    break;

                case RequestType.FileTransferAccepted:
                case RequestType.FileTransferRejected:
                case RequestType.FileTransferStalled:
                case RequestType.FileTransferComplete:
                case RequestType.RetryOutboundFileTransfer:

                    (remoteServerIpString,
                        remoteServerPortNumber,
                        fileTransferResponseCode) = ReadRequestWithInt64Value(requestBytes);

                    break;

                case RequestType.RequestedFileDoesNotExist:

                    long fileTransferIdInt64;

                    (remoteServerIpString,
                        remoteServerPortNumber,
                        fileTransferIdInt64) = ReadRequestWithInt64Value(requestBytes);

                    remoteServerTransferId = Convert.ToInt32(fileTransferIdInt64);

                    break;

                case RequestType.RetryLimitExceeded:

                    (remoteServerIpString,
                        remoteServerPortNumber,
                        remoteServerTransferId,
                        fileTransferRetryLimit,
                        lockoutExpireTimeInTicks) = ReadRetryLimitExceededRequest(requestBytes);

                    break;

                case RequestType.RetryLockoutExpired:
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
            var nextReadIndex = 1 + Constants.SizeOfInt32InBytes;

            var responseCode = BitConverter.ToInt64(requestData, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt64InBytes + Constants.SizeOfInt32InBytes;

            var transferId = BitConverter.ToInt32(requestData, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt32InBytes + Constants.SizeOfInt32InBytes;

            var retryCounter = BitConverter.ToInt32(requestData, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt32InBytes + Constants.SizeOfInt32InBytes;

            var retryLimit = BitConverter.ToInt32(requestData, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt32InBytes;

            var fileNameLen = BitConverter.ToInt32(requestData, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt32InBytes;

            var fileName = Encoding.UTF8.GetString(requestData, nextReadIndex, fileNameLen);
            nextReadIndex += fileNameLen;

            var remoteFolderLen = BitConverter.ToInt32(requestData, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt32InBytes;

            var remoteFolder = Encoding.UTF8.GetString(requestData, nextReadIndex, remoteFolderLen);
            nextReadIndex += remoteFolderLen + Constants.SizeOfInt32InBytes;

            var fileSize = BitConverter.ToInt64(requestData, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt64InBytes;

            var remoteIpLen = BitConverter.ToInt32(requestData, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt32InBytes;

            var remoteIpAddress = Encoding.UTF8.GetString(requestData, nextReadIndex, remoteIpLen);
            nextReadIndex += remoteIpLen + Constants.SizeOfInt32InBytes;

            var remotePort = BitConverter.ToInt32(requestData, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt32InBytes;

            var localFolderLen = BitConverter.ToInt32(requestData, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt32InBytes;

            var localFolder = Encoding.UTF8.GetString(requestData, nextReadIndex, localFolderLen);

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
            var nextReadIndex = 1 + Constants.SizeOfInt32InBytes;

            var remoteServerTransferId = BitConverter.ToInt32(requestData, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt32InBytes;

            var fileNameLen = BitConverter.ToInt32(requestData, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt32InBytes;

            var fileName = Encoding.UTF8.GetString(requestData, nextReadIndex, fileNameLen);
            nextReadIndex += fileNameLen;

            var localFolderLen = BitConverter.ToInt32(requestData, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt32InBytes;

            var localFolder = Encoding.UTF8.GetString(requestData, nextReadIndex, localFolderLen);
            nextReadIndex += localFolderLen;

            var remoteServerIpLen = BitConverter.ToInt32(requestData, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt32InBytes;

            var remoteServerIp = Encoding.UTF8.GetString(requestData, nextReadIndex, remoteServerIpLen);
            nextReadIndex += remoteServerIpLen + Constants.SizeOfInt32InBytes;

            var remoteServerPort = BitConverter.ToInt32(requestData, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt32InBytes;

            var remoteFolderLen = BitConverter.ToInt32(requestData, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt32InBytes;

            var remoteFolder = Encoding.UTF8.GetString(requestData, nextReadIndex, remoteFolderLen);

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
            var nextReadIndex = 1;

            var remoteServerIpLen = BitConverter.ToInt32(requestData, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt32InBytes;

            var remoteIp = Encoding.UTF8.GetString(requestData, nextReadIndex, remoteServerIpLen);
            nextReadIndex += remoteServerIpLen + Constants.SizeOfInt32InBytes;

            var remotePort = BitConverter.ToInt32(requestData, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt32InBytes;

            var targetFolderLen = BitConverter.ToInt32(requestData, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt32InBytes;

            var targetFolder = Encoding.UTF8.GetString(requestData, nextReadIndex, targetFolderLen);
            nextReadIndex += targetFolderLen;

            var fileInfoLen = BitConverter.ToInt32(requestData, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt32InBytes;

            var fileInfo = Encoding.UTF8.GetString(requestData, nextReadIndex, fileInfoLen);

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
            var nextReadIndex = 1;

            var remoteServerIpLen = BitConverter.ToInt32(requestData, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt32InBytes;

            var remoteIp = Encoding.UTF8.GetString(requestData, nextReadIndex, remoteServerIpLen);
            nextReadIndex += remoteServerIpLen + Constants.SizeOfInt32InBytes;

            var remotePort = BitConverter.ToInt32(requestData, nextReadIndex);

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
            var nextReadIndex = 1;

            var remoteServerIpLen = BitConverter.ToInt32(requestData, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt32InBytes;

            var remoteIp = Encoding.UTF8.GetString(requestData, nextReadIndex, remoteServerIpLen);
            nextReadIndex += remoteServerIpLen + Constants.SizeOfInt32InBytes;

            var remotePort = BitConverter.ToInt32(requestData, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt32InBytes + Constants.SizeOfInt32InBytes;

            var remoteServerTransferId = BitConverter.ToInt32(requestData, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt32InBytes + Constants.SizeOfInt32InBytes;

            var retryLimit = BitConverter.ToInt32(requestData, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt32InBytes + Constants.SizeOfInt32InBytes;

            var lockoutExpireTimeTicks = BitConverter.ToInt64(requestData, nextReadIndex);

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
            var nextReadIndex = 1;

            var remoteServerIpLen = BitConverter.ToInt32(requestData, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt32InBytes;

            var remoteIp = Encoding.UTF8.GetString(requestData, nextReadIndex, remoteServerIpLen);
            nextReadIndex += remoteServerIpLen + Constants.SizeOfInt32InBytes;

            var remotePort = BitConverter.ToInt32(requestData, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt32InBytes + Constants.SizeOfInt32InBytes;

            var responseCode = BitConverter.ToInt64(requestData, nextReadIndex);

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
            var nextReadIndex = 1;

            var remoteServerIpLen = BitConverter.ToInt32(requestData, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt32InBytes;

            var remoteIp = Encoding.UTF8.GetString(requestData, nextReadIndex, remoteServerIpLen);
            nextReadIndex += remoteServerIpLen + Constants.SizeOfInt32InBytes;

            var remotePort = BitConverter.ToInt32(requestData, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt32InBytes;

            var messageLen = BitConverter.ToInt32(requestData, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt32InBytes;

            var message = Encoding.UTF8.GetString(requestData, nextReadIndex, messageLen);

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
            var nextReadIndex = 1;

            var localIpLen = BitConverter.ToInt32(requestData, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt32InBytes;

            var localIp = Encoding.UTF8.GetString(requestData, nextReadIndex, localIpLen);
            nextReadIndex += localIpLen + Constants.SizeOfInt32InBytes;

            var portNumber = BitConverter.ToInt32(requestData, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt32InBytes + Constants.SizeOfInt32InBytes;

            var platformData = BitConverter.ToInt32(requestData, nextReadIndex).ToString();
            var platform = (ServerPlatform)Enum.Parse(typeof(ServerPlatform), platformData);
            nextReadIndex += Constants.SizeOfInt32InBytes;

            var publicIpLen = BitConverter.ToInt32(requestData, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt32InBytes;

            var publicIp = Encoding.UTF8.GetString(requestData, nextReadIndex, publicIpLen);
            nextReadIndex += publicIpLen;

            var transferFolderPathLen = BitConverter.ToInt32(requestData, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt32InBytes;

            var transferFolderPath = Encoding.UTF8.GetString(requestData, nextReadIndex, transferFolderPathLen);

            return (
                localIp,
                portNumber,
                platform,
                publicIp,
                transferFolderPath);
        }
    }
}
