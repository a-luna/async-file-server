using System;
using System.Collections.Generic;
using System.Text;
using AaronLuna.Common;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncSocketServer.Requests.RequestTypes
{
    public class MessageRequest : Request
    {
        public MessageRequest()
        {
            Type = RequestType.MessageRequest;
        }

        public MessageRequest(byte[] requestBytes) : base(requestBytes) { }

        public string Message { get; set; }

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

            var messageData = Encoding.UTF8.GetBytes(Message);
            var messageLen = BitConverter.GetBytes(messageData.Length);

            var requestBytes = new List<byte> {requestTypeData};
            requestBytes.AddRange(localIpLen);
            requestBytes.AddRange(localIpBytes);
            requestBytes.AddRange(localPortLen);
            requestBytes.AddRange(localPortBytes);
            requestBytes.AddRange(messageLen);
            requestBytes.AddRange(messageData);

            return Result.Ok(requestBytes.ToArray());
        }

        public override Result DecodeRequest()
        {
            var decodeRequest = base.DecodeRequest();
            if (decodeRequest.Failure)
            {
                return decodeRequest;
            }

            var readIndex = 1;

            var remoteServerIpLen = BitConverter.ToInt32(RequestBytes, readIndex);
            readIndex += remoteServerIpLen + (Constants.SizeOfInt32InBytes * 3);

            var messageLen = BitConverter.ToInt32(RequestBytes, readIndex);
            readIndex += Constants.SizeOfInt32InBytes;

            Message = Encoding.UTF8.GetString(RequestBytes, readIndex, messageLen);

            return Result.Ok();
        }
    }
}
