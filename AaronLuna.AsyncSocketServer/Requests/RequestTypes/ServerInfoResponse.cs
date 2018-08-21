using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using AaronLuna.Common;
using AaronLuna.Common.Network;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncSocketServer.Requests.RequestTypes
{
    public class ServerInfoResponse : Request
    {
        public ServerInfoResponse()
        {
            Type = RequestType.ServerInfoResponse;
        }

        public ServerInfoResponse(byte[] requestBytes) :base(requestBytes) { }

        public IPAddress LocalIpAddress { get; set; }
        public IPAddress PublicIpAddress { get; set; }
        public int PortNumber { get; set; }
        public ServerPlatform Platform { get; set; }
        public string TransferFolderPath { get; set; }

        public override Result<byte[]> EncodeRequest(ServerInfo localServerInfo)
        {
            if (Type == RequestType.None)
            {
                return Result.Fail<byte[]>(
                    $"Unable to perform the requested operation, value for request type is invalid (current value: {Type})");
            }

            string localIp = localServerInfo.LocalIpString;
            string publicIp = localServerInfo.PublicIpString;
            int portNumber = localServerInfo.PortNumber;
            var platform = localServerInfo.Platform;
            string transferFolderPath = localServerInfo.TransferFolder;

            var requestType = (byte) Type;

            var localIpBytes = Encoding.UTF8.GetBytes(localIp);
            var localIpLen = BitConverter.GetBytes(localIpBytes.Length);

            var portNumberBytes = BitConverter.GetBytes(portNumber);
            var portNumberLen = BitConverter.GetBytes(Constants.SizeOfInt32InBytes);

            var platformBytes = BitConverter.GetBytes((int) platform);
            var platformLen = BitConverter.GetBytes(Constants.SizeOfInt32InBytes);

            var publicIpBytes = Encoding.UTF8.GetBytes(publicIp);
            var publicIpLen = BitConverter.GetBytes(publicIpBytes.Length);

            var transferFolderBytes = Encoding.UTF8.GetBytes(transferFolderPath);
            var transferFolderLen = BitConverter.GetBytes(transferFolderBytes.Length);

            var requestBytes = new List<byte> {requestType};
            requestBytes.AddRange(localIpLen);
            requestBytes.AddRange(localIpBytes);
            requestBytes.AddRange(portNumberLen);
            requestBytes.AddRange(portNumberBytes);
            requestBytes.AddRange(platformLen);
            requestBytes.AddRange(platformBytes);
            requestBytes.AddRange(publicIpLen);
            requestBytes.AddRange(publicIpBytes);
            requestBytes.AddRange(transferFolderLen);
            requestBytes.AddRange(transferFolderBytes);

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

            var localIpLen = BitConverter.ToInt32(RequestBytes, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt32InBytes;

            var localIp = Encoding.UTF8.GetString(RequestBytes, nextReadIndex, localIpLen);
            nextReadIndex += localIpLen + Constants.SizeOfInt32InBytes;

            PortNumber = BitConverter.ToInt32(RequestBytes, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt32InBytes + Constants.SizeOfInt32InBytes;

            var platformBytes = BitConverter.ToInt32(RequestBytes, nextReadIndex).ToString();
            Platform = (ServerPlatform)Enum.Parse(typeof(ServerPlatform), platformBytes);
            nextReadIndex += Constants.SizeOfInt32InBytes;

            var publicIpLen = BitConverter.ToInt32(RequestBytes, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt32InBytes;

            var publicIp = Encoding.UTF8.GetString(RequestBytes, nextReadIndex, publicIpLen);
            nextReadIndex += publicIpLen;

            var transferFolderPathLen = BitConverter.ToInt32(RequestBytes, nextReadIndex);
            nextReadIndex += Constants.SizeOfInt32InBytes;

            TransferFolderPath = Encoding.UTF8.GetString(RequestBytes, nextReadIndex, transferFolderPathLen);

            LocalIpAddress = IPAddress.None;
            PublicIpAddress = IPAddress.None;

            var parseLocalIp = NetworkUtilities.ParseSingleIPv4Address(localIp);
            if (parseLocalIp.Success)
            {
                LocalIpAddress = parseLocalIp.Value;
            }

            var parsePublicIp = NetworkUtilities.ParseSingleIPv4Address(publicIp);
            if (parsePublicIp.Success)
            {
                PublicIpAddress = parsePublicIp.Value;
            }

            return Result.Ok();
        }
    }
}
