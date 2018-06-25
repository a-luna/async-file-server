namespace AaronLuna.AsyncFileServer.Utilities
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text;

    using Model;
    using Common;
    using Common.Extensions;

    public static class ServerRequestDataBuilder
    {
        public static byte[] ConstructBasicRequest(
            ServerRequestType requestType,
            string localIpAddress,
            int localPort)
        {
            var requestTypeData = BitConverter.GetBytes((int)requestType);
            var thisServerIpData = Encoding.UTF8.GetBytes(localIpAddress);
            var thisServerIpLen = BitConverter.GetBytes(thisServerIpData.Length);
            var thisServerPortData = Encoding.UTF8.GetBytes(localPort.ToString(CultureInfo.InvariantCulture));
            var thisServerPortLen = BitConverter.GetBytes(thisServerPortData.Length);

            var wrappedRequest = new List<byte>();
            wrappedRequest.AddRange(requestTypeData);
            wrappedRequest.AddRange(thisServerIpLen);
            wrappedRequest.AddRange(thisServerIpData);
            wrappedRequest.AddRange(thisServerPortLen);
            wrappedRequest.AddRange(thisServerPortData);

            return wrappedRequest.ToArray();
        }

        public static byte[] ConstructRetryLimitExceededRequest(
            string localIpAddress,
            int localPort,
            int fileTransferId,
            int retryLimit,
            long lockoutExpireTimeTicks)
        {
            var requestTypeData = BitConverter.GetBytes((int)ServerRequestType.RetryLimitExceeded);
            var thisServerIpData = Encoding.UTF8.GetBytes(localIpAddress);
            var thisServerIpLen = BitConverter.GetBytes(thisServerIpData.Length);
            var thisServerPortData = BitConverter.GetBytes(localPort);
            var thisServerPortLen = BitConverter.GetBytes(Constants.SizeOfInt32InBytes);
            var idData = BitConverter.GetBytes(fileTransferId);
            var idLen = BitConverter.GetBytes(Constants.SizeOfInt32InBytes);
            var retryLimitData = BitConverter.GetBytes(retryLimit);
            var retryLimitLen = BitConverter.GetBytes(Constants.SizeOfInt32InBytes);
            var lockoutExpireTimeData = BitConverter.GetBytes(lockoutExpireTimeTicks);
            var lockoutExpireTimeLen = BitConverter.GetBytes(Constants.SizeOfInt64InBytes);

            var wrappedRequest = new List<byte>();
            wrappedRequest.AddRange(requestTypeData);
            wrappedRequest.AddRange(thisServerIpLen);
            wrappedRequest.AddRange(thisServerIpData);
            wrappedRequest.AddRange(thisServerPortLen);
            wrappedRequest.AddRange(thisServerPortData);
            wrappedRequest.AddRange(idLen);
            wrappedRequest.AddRange(idData);
            wrappedRequest.AddRange(retryLimitLen);
            wrappedRequest.AddRange(retryLimitData);
            wrappedRequest.AddRange(lockoutExpireTimeLen);
            wrappedRequest.AddRange(lockoutExpireTimeData);

            return wrappedRequest.ToArray();
        }

        public static byte[] ConstructRequestWithInt64Value(
            ServerRequestType requestType,
            string localIpAddress,
            int localPort,
            long responseCode)
        {
            var requestTypeData = BitConverter.GetBytes((int)requestType);
            var thisServerIpData = Encoding.UTF8.GetBytes(localIpAddress);
            var thisServerIpLen = BitConverter.GetBytes(thisServerIpData.Length);
            var thisServerPortData = BitConverter.GetBytes(localPort);
            var thisServerPortLen = BitConverter.GetBytes(Constants.SizeOfInt32InBytes);
            var idData = BitConverter.GetBytes(responseCode);
            var idLen = BitConverter.GetBytes(Constants.SizeOfInt64InBytes);

            var wrappedRequest = new List<byte>();
            wrappedRequest.AddRange(requestTypeData);
            wrappedRequest.AddRange(thisServerIpLen);
            wrappedRequest.AddRange(thisServerIpData);
            wrappedRequest.AddRange(thisServerPortLen);
            wrappedRequest.AddRange(thisServerPortData);
            wrappedRequest.AddRange(idLen);
            wrappedRequest.AddRange(idData);

            return wrappedRequest.ToArray();
        }

        public static byte[] ConstructRequestWithStringValue(
            ServerRequestType requestType,
            string localIpAddress,
            int localPort,
            string message)
        {
            var requestTypeData = BitConverter.GetBytes((int)requestType);
            var thisServerIpData = Encoding.UTF8.GetBytes(localIpAddress);
            var thisServerIpLen = BitConverter.GetBytes(thisServerIpData.Length);
            var thisServerPortData = Encoding.UTF8.GetBytes(localPort.ToString(CultureInfo.InvariantCulture));
            var thisServerPortLen = BitConverter.GetBytes(thisServerPortData.Length);
            var messageData = Encoding.UTF8.GetBytes(message);
            var messageLen = BitConverter.GetBytes(messageData.Length);

            var wrappedRequest = new List<byte>();
            wrappedRequest.AddRange(requestTypeData);
            wrappedRequest.AddRange(thisServerIpLen);
            wrappedRequest.AddRange(thisServerIpData);
            wrappedRequest.AddRange(thisServerPortLen);
            wrappedRequest.AddRange(thisServerPortData);
            wrappedRequest.AddRange(messageLen);
            wrappedRequest.AddRange(messageData);

            return wrappedRequest.ToArray();
        }

        public static byte[] ConstructInboundFileTransferRequest(
            string localIpAddress,
            int localPort,
            FileTransfer fileTransfer)
        {
            var fileTransferId = fileTransfer.Id;
            var remoteFilePath = fileTransfer.RemoteFilePath;
            var localFolderPath = fileTransfer.LocalFolderPath;

            var requestType = BitConverter.GetBytes((int)ServerRequestType.OutboundFileTransferRequest);

            var fileTransferIdData = BitConverter.GetBytes(fileTransferId);
            var fileTransferIdLen = BitConverter.GetBytes(Constants.SizeOfInt32InBytes);

            var remoteFilePathData = Encoding.UTF8.GetBytes(remoteFilePath);
            var remoteFilePathLen = BitConverter.GetBytes(remoteFilePathData.Length);

            var thisServerIpData = Encoding.UTF8.GetBytes(localIpAddress);
            var thisServerIpLen = BitConverter.GetBytes(thisServerIpData.Length);

            var thisServerPortData = BitConverter.GetBytes(localPort);
            var thisServerPortLen = BitConverter.GetBytes(Constants.SizeOfInt32InBytes);

            var targetDirData = Encoding.UTF8.GetBytes(localFolderPath);
            var targetDirLen = BitConverter.GetBytes(targetDirData.Length);

            var wrappedRequest = new List<byte>();
            wrappedRequest.AddRange(requestType);
            wrappedRequest.AddRange(fileTransferIdLen);
            wrappedRequest.AddRange(fileTransferIdData);
            wrappedRequest.AddRange(remoteFilePathLen);
            wrappedRequest.AddRange(remoteFilePathData);
            wrappedRequest.AddRange(thisServerIpLen);
            wrappedRequest.AddRange(thisServerIpData);
            wrappedRequest.AddRange(thisServerPortLen);
            wrappedRequest.AddRange(thisServerPortData);
            wrappedRequest.AddRange(targetDirLen);
            wrappedRequest.AddRange(targetDirData);

            return wrappedRequest.ToArray();
        }

        public static byte[] ConstructOutboundFileTransferRequest(
            string localIpAddress,
            int localPort,
            FileTransfer fileTransfer)
        {
            var responseCode = fileTransfer.TransferResponseCode;
            var remoteServerTransferId = fileTransfer.RemoteServerTransferId;
            var retryCounter = fileTransfer.RetryCounter;
            var retryLimit = fileTransfer.RemoteServerRetryLimit;
            var localFilePath = fileTransfer.LocalFilePath;
            var fileSizeBytes = fileTransfer.FileSizeInBytes;
            var remoteFolderPath = fileTransfer.RemoteFolderPath;

            var requestType = BitConverter.GetBytes((int)ServerRequestType.InboundFileTransferRequest);

            var responseCodeData = BitConverter.GetBytes(responseCode);
            var responseCodeLen = BitConverter.GetBytes(Constants.SizeOfInt64InBytes);

            var remoteServerTransferIdData = BitConverter.GetBytes(remoteServerTransferId);
            var remoteServerTransferIdLen = BitConverter.GetBytes(Constants.SizeOfInt32InBytes);

            var retryCounterData = BitConverter.GetBytes(retryCounter);
            var retryCounterLen = BitConverter.GetBytes(Constants.SizeOfInt32InBytes);

            var retryLimitData = BitConverter.GetBytes(retryLimit);
            var retryLimitLen = BitConverter.GetBytes(Constants.SizeOfInt32InBytes);

            var fileName = Path.GetFileName(localFilePath);
            var fileNameData = Encoding.UTF8.GetBytes(fileName);
            var fileNameLen = BitConverter.GetBytes(fileNameData.Length);

            var sizeBytesData = BitConverter.GetBytes(fileSizeBytes);
            var sizeBytesLen = BitConverter.GetBytes(Constants.SizeOfInt64InBytes);

            var remoteIpData = Encoding.UTF8.GetBytes(localIpAddress);
            var remoteIpLen = BitConverter.GetBytes(remoteIpData.Length);

            var remotePortData = BitConverter.GetBytes(localPort);
            var remotePortLen = BitConverter.GetBytes(Constants.SizeOfInt32InBytes);

            var targetDirData = Encoding.UTF8.GetBytes(remoteFolderPath);
            var targetDirLen = BitConverter.GetBytes(targetDirData.Length);

            var wrappedRequest = new List<byte>();
            wrappedRequest.AddRange(requestType);
            wrappedRequest.AddRange(responseCodeLen);
            wrappedRequest.AddRange(responseCodeData);
            wrappedRequest.AddRange(remoteServerTransferIdLen);
            wrappedRequest.AddRange(remoteServerTransferIdData);
            wrappedRequest.AddRange(retryCounterLen);
            wrappedRequest.AddRange(retryCounterData);
            wrappedRequest.AddRange(retryLimitLen);
            wrappedRequest.AddRange(retryLimitData);
            wrappedRequest.AddRange(fileNameLen);
            wrappedRequest.AddRange(fileNameData);
            wrappedRequest.AddRange(sizeBytesLen);
            wrappedRequest.AddRange(sizeBytesData);
            wrappedRequest.AddRange(remoteIpLen);
            wrappedRequest.AddRange(remoteIpData);
            wrappedRequest.AddRange(remotePortLen);
            wrappedRequest.AddRange(remotePortData);
            wrappedRequest.AddRange(targetDirLen);
            wrappedRequest.AddRange(targetDirData);

            return wrappedRequest.ToArray();
        }

        public static byte[] ConstructFileListResponse(
            FileInfoList fileInfoList,
            string fileInfoSeparator,
            string fileSeparator,
            string localIpAddress,
            int localPort,
            string remoteFolderPath)
        {
            var requestType = BitConverter.GetBytes((int)ServerRequestType.FileListResponse);

            var localServerIpData = Encoding.UTF8.GetBytes(localIpAddress);
            var localServerIpLen = BitConverter.GetBytes(localServerIpData.Length);

            var localServerPortData = Encoding.UTF8.GetBytes(localPort.ToString(CultureInfo.InvariantCulture));
            var localServerPortLen = BitConverter.GetBytes(localServerPortData.Length);

            var requestorFolderPathData = Encoding.UTF8.GetBytes(remoteFolderPath);
            var requestorFolderPathLen = BitConverter.GetBytes(requestorFolderPathData.Length);

            var allFileInfo = string.Empty;
            foreach (var i in Enumerable.Range(0, fileInfoList.Count))
            {
                var filePath = fileInfoList[i].filePath;
                var fileSize = fileInfoList[i].fileSizeBytes;
                var fileInfoString = $"{filePath}{fileInfoSeparator}{fileSize}";

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

            var wrappedRequest = new List<byte>();
            wrappedRequest.AddRange(requestType);
            wrappedRequest.AddRange(localServerIpLen);
            wrappedRequest.AddRange(localServerIpData);
            wrappedRequest.AddRange(localServerPortLen);
            wrappedRequest.AddRange(localServerPortData);
            wrappedRequest.AddRange(requestorFolderPathLen);
            wrappedRequest.AddRange(requestorFolderPathData);
            wrappedRequest.AddRange(fileInfoListLen);
            wrappedRequest.AddRange(fileInfoListData);
            wrappedRequest.AddRange(fileInfoSeparatorLen);
            wrappedRequest.AddRange(fileInfoSeparatorData);
            wrappedRequest.AddRange(fileSeparatorLen);
            wrappedRequest.AddRange(fileSeparatorData);

            return wrappedRequest.ToArray();
        }

        public static byte[] ConstructServerInfoResponse(
            string localIpAddress,
            int localPort,
            string publicIp,
            string transferFolder)
        {
            var requestType = BitConverter.GetBytes((int)ServerRequestType.ServerInfoResponse);
            var thisServerIpData = Encoding.UTF8.GetBytes(localIpAddress);
            var thisServerIpLen = BitConverter.GetBytes(thisServerIpData.Length);
            var thisServerPortData = Encoding.UTF8.GetBytes(localPort.ToString(CultureInfo.InvariantCulture));
            var thisServerPortLen = BitConverter.GetBytes(thisServerPortData.Length);
            var publicIpData = Encoding.UTF8.GetBytes(publicIp);
            var publicIpLen = BitConverter.GetBytes(publicIpData.Length);
            var transferFolderData = Encoding.UTF8.GetBytes(transferFolder);
            var transferFolderLen = BitConverter.GetBytes(transferFolderData.Length);

            var wrappedRequest = new List<byte>();
            wrappedRequest.AddRange(requestType);
            wrappedRequest.AddRange(thisServerIpLen);
            wrappedRequest.AddRange(thisServerIpData);
            wrappedRequest.AddRange(thisServerPortLen);
            wrappedRequest.AddRange(thisServerPortData);
            wrappedRequest.AddRange(publicIpLen);
            wrappedRequest.AddRange(publicIpData);
            wrappedRequest.AddRange(transferFolderLen);
            wrappedRequest.AddRange(transferFolderData);

            return wrappedRequest.ToArray();
        }
    }
}
