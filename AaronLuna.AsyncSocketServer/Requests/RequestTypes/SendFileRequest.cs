using System;
using System.Collections.Generic;
using System.Text;
using AaronLuna.Common;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncSocketServer.Requests.RequestTypes
{
    public class SendFileRequest : Request
    {
        public SendFileRequest()
        {
            Type = RequestType.InboundFileTransferRequest;
        }

        public SendFileRequest(byte[] requestBytes) : base(requestBytes) { }

        public long FileTransferResponseCode { get; set; }
        public int RemoteServerTransferId { get; set; }
        public int RetryCounter { get; set; }
        public int RetryLimit { get; set; }
        public string FileName { get; set; }
        public long FileSizeInBytes { get; set; }
        public string RemoteFolderPath { get; set; }
        public string LocalFolderPath { get; set; }

        public override Result<byte[]> EncodeRequest(ServerInfo localServerInfo)
        {
            if (Type == RequestType.None)
            {
                return Result.Fail<byte[]>(
                    $"Unable to perform the requested operation, value for request type is invalid (current value: {Type})");
            }

            var localIp = localServerInfo.LocalIpString;
            var localPort = localServerInfo.PortNumber;

            var requestType = (byte) Type;

            var responseCodeData = BitConverter.GetBytes(FileTransferResponseCode);
            var responseCodeLen = BitConverter.GetBytes(Constants.SizeOfInt64InBytes);

            var remoteServerTransferIdData = BitConverter.GetBytes(RemoteServerTransferId);
            var remoteServerTransferIdLen = BitConverter.GetBytes(Constants.SizeOfInt32InBytes);

            var retryCounterData = BitConverter.GetBytes(RetryCounter);
            var retryCounterLen = BitConverter.GetBytes(Constants.SizeOfInt32InBytes);

            var retryLimitData = BitConverter.GetBytes(RetryLimit);
            var retryLimitLen = BitConverter.GetBytes(Constants.SizeOfInt32InBytes);

            var fileNameData = Encoding.UTF8.GetBytes(FileName);
            var fileNameLen = BitConverter.GetBytes(fileNameData.Length);

            var localFolderData = Encoding.UTF8.GetBytes(LocalFolderPath);
            var localFolderLen = BitConverter.GetBytes(localFolderData.Length);

            var sizeBytesData = BitConverter.GetBytes(FileSizeInBytes);
            var sizeBytesLen = BitConverter.GetBytes(Constants.SizeOfInt64InBytes);

            var remoteIpData = Encoding.UTF8.GetBytes(localIp);
            var remoteIpLen = BitConverter.GetBytes(remoteIpData.Length);

            var remotePortData = BitConverter.GetBytes(localPort);
            var remotePortLen = BitConverter.GetBytes(Constants.SizeOfInt32InBytes);

            var targetDirData = Encoding.UTF8.GetBytes(RemoteFolderPath);
            var targetDirLen = BitConverter.GetBytes(targetDirData.Length);

            var requestBytes = new List<byte> {requestType};
            requestBytes.AddRange(responseCodeLen);
            requestBytes.AddRange(responseCodeData);
            requestBytes.AddRange(remoteServerTransferIdLen);
            requestBytes.AddRange(remoteServerTransferIdData);
            requestBytes.AddRange(retryCounterLen);
            requestBytes.AddRange(retryCounterData);
            requestBytes.AddRange(retryLimitLen);
            requestBytes.AddRange(retryLimitData);
            requestBytes.AddRange(fileNameLen);
            requestBytes.AddRange(fileNameData);
            requestBytes.AddRange(localFolderLen);
            requestBytes.AddRange(localFolderData);
            requestBytes.AddRange(sizeBytesLen);
            requestBytes.AddRange(sizeBytesData);
            requestBytes.AddRange(remoteIpLen);
            requestBytes.AddRange(remoteIpData);
            requestBytes.AddRange(remotePortLen);
            requestBytes.AddRange(remotePortData);
            requestBytes.AddRange(targetDirLen);
            requestBytes.AddRange(targetDirData);

            return Result.Ok(requestBytes.ToArray());
        }

        public override Result DecodeRequest()
        {
            var decodeRequest = base.DecodeRequest();
            if (decodeRequest.Failure)
            {
                return decodeRequest;
            }

            var nextReadIndex = 1 + Constants.SizeOfInt32InBytes;

            FileTransferResponseCode = BitConverter.ToInt64(RequestBytes, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt32InBytes + Constants.SizeOfInt64InBytes;

            RemoteServerTransferId = BitConverter.ToInt32(RequestBytes, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt32InBytes + Constants.SizeOfInt32InBytes;

            RetryCounter = BitConverter.ToInt32(RequestBytes, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt32InBytes + Constants.SizeOfInt32InBytes;

            RetryLimit = BitConverter.ToInt32(RequestBytes, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt32InBytes;

            var fileNameLen = BitConverter.ToInt32(RequestBytes, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt32InBytes;

            FileName = Encoding.UTF8.GetString(RequestBytes, nextReadIndex, fileNameLen);
            nextReadIndex += fileNameLen;

            var remoteFolderLen = BitConverter.ToInt32(RequestBytes, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt32InBytes;

            RemoteFolderPath = Encoding.UTF8.GetString(RequestBytes, nextReadIndex, remoteFolderLen);
            nextReadIndex += remoteFolderLen + Constants.SizeOfInt32InBytes;

            FileSizeInBytes = BitConverter.ToInt64(RequestBytes, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt64InBytes;

            var remoteIpLen = BitConverter.ToInt32(RequestBytes, nextReadIndex);
            nextReadIndex += remoteIpLen + (Constants.SizeOfInt32InBytes * 3);

            var localFolderLen = BitConverter.ToInt32(RequestBytes, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt32InBytes;

            LocalFolderPath = Encoding.UTF8.GetString(RequestBytes, nextReadIndex, localFolderLen);

            return Result.Ok();
        }
    }
}
