namespace TplSockets
{
    using System;
    using System.Collections.Generic;
    using System.Net;

    public class Message
    {
        public Message()
        {
            EventLog = new List<ServerEvent>();
            Type = MessageType.None;
        }

        public int Id { get; set; }
        public DateTime Timestamp { get; set; }
        public byte[] Data { get; set; }
        public MessageType Type { get; set; }
        public IPAddress RemoteServerIp { get; set; }
        public List<ServerEvent> EventLog { get; set; }

        public override string ToString()
        {
            return $"{Type.Name()} from {RemoteServerIp} at {Timestamp:g}";
        }

        public bool MustBeProcessedImmediately()
        {
            return Type.MustBeProcessedImmediately();
        }
    }

    public static class MessageExtensions
    {
        public static bool MustBeProcessedImmediately(this MessageType messageType)
        {
            switch (messageType)
            {
                case MessageType.None:
                case MessageType.TextMessage:
                case MessageType.InboundFileTransferRequest:
                    return false;

                case MessageType.ServerInfoRequest:
                case MessageType.ServerInfoResponse:
                case MessageType.FileListRequest:
                case MessageType.FileListResponse:
                case MessageType.OutboundFileTransferRequest:
                case MessageType.NoFilesAvailableForDownload:
                case MessageType.RequestedFolderDoesNotExist:
                case MessageType.FileTransferAccepted:
                case MessageType.FileTransferRejected:
                case MessageType.FileTransferStalled:
                case MessageType.RetryOutboundFileTransfer:
                case MessageType.ShutdownServerCommand:
                    return true;

                default:
                    return false;
            }
        }

        public static bool MustBeProcessedImmediately(this Message message)
        {
            return message.Type.MustBeProcessedImmediately();
        }
    }
}
