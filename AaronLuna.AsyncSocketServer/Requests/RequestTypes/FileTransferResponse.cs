using System;
using System.Collections.Generic;
using System.Text;
using AaronLuna.Common;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncSocketServer.Requests.RequestTypes
{
    public class FileTransferResponse : Request
    {
        public FileTransferResponse(RequestType requestType)
        {
            Type = requestType;
        }

        public FileTransferResponse(byte[] requestBytes) :base(requestBytes) { }

        public long TransferResponseCode { get; set; }
        public int RemoteServerTransferId { get; set; }

        public override Result<byte[]> EncodeRequest(ServerInfo localServerInfo)
        {
            if (Type == RequestType.None)
            {
                return Result.Fail<byte[]>(
                    $"Unable to perform the requested operation, value for request type is invalid (current value: {Type})");
            }

            string localIp = localServerInfo.LocalIpString;
            int localPort = localServerInfo.PortNumber;

            var requestTypeData = (byte) Type;

            var localIpBytes = Encoding.UTF8.GetBytes(localIp);
            var localIpLen = BitConverter.GetBytes(localIpBytes.Length);

            var localPortBytes = BitConverter.GetBytes(localPort);
            var localPortLen = BitConverter.GetBytes(Constants.SizeOfInt32InBytes);

            var responseCodeBytes = BitConverter.GetBytes(TransferResponseCode);
            var responseCodeLen = BitConverter.GetBytes(Constants.SizeOfInt64InBytes);

            var transferIdBytes = BitConverter.GetBytes(RemoteServerTransferId);
            var transferIdLen = BitConverter.GetBytes(Constants.SizeOfInt32InBytes);

            var requestBytes = new List<byte> { requestTypeData };
            requestBytes.AddRange(localIpLen);
            requestBytes.AddRange(localIpBytes);
            requestBytes.AddRange(localPortLen);
            requestBytes.AddRange(localPortBytes);
            requestBytes.AddRange(responseCodeLen);
            requestBytes.AddRange(responseCodeBytes);
            requestBytes.AddRange(transferIdLen);
            requestBytes.AddRange(transferIdBytes);

            return Result.Ok(requestBytes.ToArray());
        }

        public override Result DecodeRequest()
        {
            var decodeRequest = base.DecodeRequest();
            if (decodeRequest.Failure)
            {
                return decodeRequest;
            }

            var nextReadIndex = 1;

            var remoteServerIpLen = BitConverter.ToInt32(RequestBytes, nextReadIndex);
            nextReadIndex += remoteServerIpLen + (Constants.SizeOfInt32InBytes * 4);

            TransferResponseCode = BitConverter.ToInt64(RequestBytes, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt64InBytes + Constants.SizeOfInt32InBytes;

            RemoteServerTransferId = BitConverter.ToInt32(RequestBytes, nextReadIndex);

            return Result.Ok();
        }
    }
}
