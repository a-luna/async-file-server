using System;
using System.Collections.Generic;
using System.Text;
using AaronLuna.Common;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncSocketServer.Requests.RequestTypes
{
    public class RetryLimitExceeded : Request
    {
        public RetryLimitExceeded()
        {
            Type = RequestType.RetryLimitExceeded;
        }

        public RetryLimitExceeded(byte[] requestBytes) : base(requestBytes) { }

        public int RemoteServerTransferId { get; set; }
        public int RetryLimit { get; set; }
        public DateTime LockoutExpireTime { get; set; }

        public override Result<byte[]> EncodeRequest(ServerInfo localServerInfo)
        {
            if (Type == RequestType.None)
            {
                return Result.Fail<byte[]>(
                    $"Unable to perform the requested operation, value for request type is invalid (current value: {Type})");
            }

            string localIp = localServerInfo.LocalIpString;
            int localPort = localServerInfo.PortNumber;

            var requestTypeData = (byte)Type;

            var localIpBytes = Encoding.UTF8.GetBytes(localIp);
            var localIpLen = BitConverter.GetBytes(localIpBytes.Length);

            var localPortBytes = BitConverter.GetBytes(localPort);
            var localPortLen = BitConverter.GetBytes(Constants.SizeOfInt32InBytes);

            var transferIdBytes = BitConverter.GetBytes(RemoteServerTransferId);
            var transferIdLen = BitConverter.GetBytes(Constants.SizeOfInt32InBytes);

            var retryLimitBytes = BitConverter.GetBytes(RetryLimit);
            var retryLimitLen = BitConverter.GetBytes(Constants.SizeOfInt32InBytes);

            var lockoutExpireTimeBytes = BitConverter.GetBytes(LockoutExpireTime.Ticks);
            var lockoutExpireTimeLen = BitConverter.GetBytes(Constants.SizeOfInt64InBytes);

            var requestBytes = new List<byte> { requestTypeData };
            requestBytes.AddRange(localIpLen);
            requestBytes.AddRange(localIpBytes);
            requestBytes.AddRange(localPortLen);
            requestBytes.AddRange(localPortBytes);
            requestBytes.AddRange(transferIdLen);
            requestBytes.AddRange(transferIdBytes);
            requestBytes.AddRange(retryLimitLen);
            requestBytes.AddRange(retryLimitBytes);
            requestBytes.AddRange(lockoutExpireTimeLen);
            requestBytes.AddRange(lockoutExpireTimeBytes);

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

            RemoteServerTransferId = BitConverter.ToInt32(RequestBytes, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt32InBytes + Constants.SizeOfInt32InBytes;

            RetryLimit = BitConverter.ToInt32(RequestBytes, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt32InBytes + Constants.SizeOfInt32InBytes;

            var lockoutExpireTimeTicks = BitConverter.ToInt64(RequestBytes, nextReadIndex);
            LockoutExpireTime = new DateTime(lockoutExpireTimeTicks);

            return Result.Ok();
        }
    }
}
