using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AaronLuna.AsyncSocketServer.FileTransfers;
using AaronLuna.Common;
using AaronLuna.Common.Extensions;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncSocketServer.Requests.RequestTypes
{
    public class FileListResponse : Request
    {
        public FileListResponse()
        {
            Type = RequestType.FileListResponse;
        }

        public FileListResponse(byte[] requestBytes) : base(requestBytes) { }

        public FileInfoList FileInfoList { get; set; }

        public override Result<byte[]> EncodeRequest(ServerInfo localServerInfo)
        {
            if (Type == RequestType.None)
            {
                return Result.Fail<byte[]>(
                    $"Unable to perform the requested operation, value for request type is invalid (current value: {Type})");
            }

            string localIp = localServerInfo.LocalIpString;
            int localPort = localServerInfo.PortNumber;
            string localFolderPath = localServerInfo.TransferFolder;

            var requestType = (byte) Type;

            var localIpBytes = Encoding.UTF8.GetBytes(localIp);
            var localIpLen = BitConverter.GetBytes(localIpBytes.Length);

            var localPortBytes = BitConverter.GetBytes(localPort);
            var localPortLen = BitConverter.GetBytes(Constants.SizeOfInt32InBytes);

            var localFOlderPathBytes = Encoding.UTF8.GetBytes(localFolderPath);
            var localFolderPathLen = BitConverter.GetBytes(localFOlderPathBytes.Length);

            var allFileInfo = string.Empty;
            foreach (var i in Enumerable.Range(0, FileInfoList.Count))
            {
                var fileName = FileInfoList[i].fileName;
                var folderPath = FileInfoList[i].folderPath;
                var fileSize = FileInfoList[i].fileSizeBytes;
                var fileInfoString = $"{fileName}{FileInfoList.FileInfoSeparator}{folderPath}{FileInfoList.FileInfoSeparator}{fileSize}";

                allFileInfo += fileInfoString;

                if (!i.IsLastIteration(FileInfoList.Count))
                {
                    allFileInfo += FileInfoList.FileSeparator;
                }
            }

            var fileInfoListData = Encoding.UTF8.GetBytes(allFileInfo);
            var fileInfoListLen = BitConverter.GetBytes(fileInfoListData.Length);

            var requestBytes = new List<byte> {requestType};
            requestBytes.AddRange(localIpLen);
            requestBytes.AddRange(localIpBytes);
            requestBytes.AddRange(localPortLen);
            requestBytes.AddRange(localPortBytes);
            requestBytes.AddRange(localFolderPathLen);
            requestBytes.AddRange(localFOlderPathBytes);
            requestBytes.AddRange(fileInfoListLen);
            requestBytes.AddRange(fileInfoListData);

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

            var targetFolderLen = BitConverter.ToInt32(RequestBytes, readIndex);
            readIndex += targetFolderLen + Constants.SizeOfInt32InBytes;

            var fileInfoLen = BitConverter.ToInt32(RequestBytes, readIndex);
            readIndex += Constants.SizeOfInt32InBytes;

            var fileInfo = Encoding.UTF8.GetString(RequestBytes, readIndex, fileInfoLen);
            FileInfoList = FileInfoList.Parse(fileInfo);

            return Result.Ok();
        }
    }
}
