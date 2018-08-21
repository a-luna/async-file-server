using System;
using System.Collections.Generic;
using System.Text;
using AaronLuna.AsyncSocketServer.FileTransfers;
using AaronLuna.Common;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncSocketServer.Requests
{
    public class Request
    {
        protected static readonly string ErrorNoDataReceived =
            "Request from remote server has not been received.";

        private bool _idHasBeenSet;
        protected byte[] RequestBytes;

        public Request()
        {
            TimeStamp = DateTime.Now;
            Status = RequestStatus.NoData;
            Type = RequestType.None;
            Direction = TransferDirection.None;
            RemoteServerInfo = new ServerInfo();
            LocalServerInfo = new ServerInfo();

            _idHasBeenSet = false;
        }

        public Request(RequestType requestType) :this()
        {
            Type = requestType;
        }

        public Request(byte[] requestBytes) :this()
        {
            RequestBytes = requestBytes;
        }

        public int Id { get; private set; }
        public DateTime TimeStamp { get; private set; }
        public RequestStatus Status { get; set; }
        public RequestType Type { get; protected set; }
        public TransferDirection Direction { get; set; }
        public ServerInfo LocalServerInfo { get; set; }
        public ServerInfo RemoteServerInfo { get; set; }

        //public bool HasBeenProcessed => Status.RequestHasBeenProcesed();
        //public bool IsLongRunningProcess => Type.IsLongRunningProcess();
        //public bool InboundTransferIsRequested => Type == RequestType.InboundFileTransferRequest;
        //public bool OutboundTransferIsRequested => Type == RequestType.OutboundFileTransferRequest;
        //public bool IsFileTransferResponse => Type.IsFileTransferResponse();
        //public bool IsFileTransferError => Type.IsFileTransferError();

        public virtual Result<byte[]> EncodeRequest(ServerInfo localServerInfo)
        {
            if (Type == RequestType.None)
            {
                return Result.Fail<byte[]>(
                    $"Unable to perform the requested operation, value for request type is invalid (current value: {Type})");
            }

            LocalServerInfo = localServerInfo;

            string localIp = LocalServerInfo.LocalIpString;
            int localPort = LocalServerInfo.PortNumber;

            var requestType = (byte) Type;

            var localIpBytes = Encoding.UTF8.GetBytes(localIp);
            var localIpLen = BitConverter.GetBytes(localIpBytes.Length);

            var localPortBytes = BitConverter.GetBytes(localPort);
            var localPortLen = BitConverter.GetBytes(Constants.SizeOfInt32InBytes);

            var requestBytes = new List<byte> {requestType};
            requestBytes.AddRange(localIpLen);
            requestBytes.AddRange(localIpBytes);
            requestBytes.AddRange(localPortLen);
            requestBytes.AddRange(localPortBytes);

            return Result.Ok(requestBytes.ToArray());
        }

        public virtual Result DecodeRequest()
        {
            if (RequestBytes == null)
            {
                return Result.Fail("Unable to perform the requested operation, no binary data has been received");
            }

            Type = RequestDecoder.ReadRequestType(RequestBytes);
            RemoteServerInfo = RequestDecoder.ReadRemoteServerInfo(RequestBytes);

            return Result.Ok();
        }

        public void SetId(int id)
        {
            if (_idHasBeenSet) return;

            Id = id;
            _idHasBeenSet = true;
        }

        public string ItemText(int itemNumber)
        {
            if (Direction == TransferDirection.None) return string.Empty;
            if (Type == RequestType.None) return string.Empty;
            if (Status == RequestStatus.NoData) return string.Empty;

            var space = itemNumber >= 10
                ? string.Empty
                : " ";

            var direction = string.Empty;
            var timeStamp = string.Empty;

            switch (Direction)
            {
                case TransferDirection.Outbound:
                    direction = "Sent To........:";
                    timeStamp = "Sent At........:";
                    break;

                case TransferDirection.Inbound:
                    direction = "Received From..:";
                    timeStamp = "Received At....:";
                    break;
            }

            return $"{space}Request Type...: {Type.Name()} [{Status}]{Environment.NewLine}" +
                   $"    {direction} {RemoteServerInfo}{Environment.NewLine}" +
                   $"    {timeStamp} {TimeStamp:MM/dd/yyyy hh:mm tt}{Environment.NewLine}";
        }

        public override string ToString()
        {
            return ItemText(0);
        }

        public Request Duplicate()
        {
            var copy = new Request()
            {
                TimeStamp = new DateTime(TimeStamp.Ticks),
                Status = Status,
                Direction = Direction,
                Type = Type,
                RemoteServerInfo = RemoteServerInfo.Duplicate(),
                LocalServerInfo = LocalServerInfo.Duplicate()
            };

            copy.SetId(Id);

            return copy;
        }
    }
}
