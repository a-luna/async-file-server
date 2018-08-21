using System;
using System.Collections.Generic;
using System.Text;
using AaronLuna.Common;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncSocketServer.Requests.RequestTypes
{
    public class GetFileRequest : Request
    {
        public GetFileRequest()
        {
            Type = RequestType.OutboundFileTransferRequest;
        }

        public GetFileRequest(byte[] requestBytes) : base(requestBytes) { }

        public int RemoteServerTransferId { get; set; }
        public string FileName { get; set; }
        public string LocalFolderPath { get; set; }
        public string RemoteFolderPath { get; set; }

        public override Result<byte[]> EncodeRequest(ServerInfo localServerInfo)
        {
            if (Type == RequestType.None)
            {
                return Result.Fail<byte[]>(
                    $"Unable to perform the requested operation, value for request type is invalid (current value: {Type})");
            }

            string localIp = localServerInfo.LocalIpString;
            int localPort = localServerInfo.PortNumber;

            var requestType = (byte) Type;

            var fileTransferIdBytes = BitConverter.GetBytes(RemoteServerTransferId);
            var fileTransferIdLen = BitConverter.GetBytes(Constants.SizeOfInt32InBytes);

            var fileNameBytes = Encoding.UTF8.GetBytes(FileName);
            var nameLen = BitConverter.GetBytes(fileNameBytes.Length);

            var remoteFolderPathBytes = Encoding.UTF8.GetBytes(RemoteFolderPath);
            var remoteFolderPathLen = BitConverter.GetBytes(remoteFolderPathBytes.Length);

            var localIpBytes = Encoding.UTF8.GetBytes(localIp);
            var localIpLen = BitConverter.GetBytes(localIpBytes.Length);

            var localPortBytes = BitConverter.GetBytes(localPort);
            var localPortLen = BitConverter.GetBytes(Constants.SizeOfInt32InBytes);

            var localFolderPathBytes = Encoding.UTF8.GetBytes(LocalFolderPath);
            var localFolderPathLen = BitConverter.GetBytes(localFolderPathBytes.Length);

            var requestBytes = new List<byte> {requestType};
            requestBytes.AddRange(fileTransferIdLen);
            requestBytes.AddRange(fileTransferIdBytes);
            requestBytes.AddRange(nameLen);
            requestBytes.AddRange(fileNameBytes);
            requestBytes.AddRange(remoteFolderPathLen);
            requestBytes.AddRange(remoteFolderPathBytes);
            requestBytes.AddRange(localIpLen);
            requestBytes.AddRange(localIpBytes);
            requestBytes.AddRange(localPortLen);
            requestBytes.AddRange(localPortBytes);
            requestBytes.AddRange(localFolderPathLen);
            requestBytes.AddRange(localFolderPathBytes);

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

            RemoteServerTransferId = BitConverter.ToInt32(RequestBytes, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt32InBytes;

            var fileNameLen = BitConverter.ToInt32(RequestBytes, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt32InBytes;

            FileName = Encoding.UTF8.GetString(RequestBytes, nextReadIndex, fileNameLen);
            nextReadIndex += fileNameLen;

            var localFolderLen = BitConverter.ToInt32(RequestBytes, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt32InBytes;

            LocalFolderPath = Encoding.UTF8.GetString(RequestBytes, nextReadIndex, localFolderLen);
            nextReadIndex += localFolderLen;

            var remoteServerIpLen = BitConverter.ToInt32(RequestBytes, nextReadIndex);
            nextReadIndex += remoteServerIpLen + (Constants.SizeOfInt32InBytes * 3);

            var remoteFolderLen = BitConverter.ToInt32(RequestBytes, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt32InBytes;

            RemoteFolderPath = Encoding.UTF8.GetString(RequestBytes, nextReadIndex, remoteFolderLen);

            return Result.Ok();
        }
    }
}
