namespace TplSockets
{
    using System.Collections.Generic;

    public class Message
    {
        public Message()
        {
            EventLog = new List<ServerEvent>();
            Type = MessageType.None;
        }

        public int Id { get; set; }
        public byte[] Data { get; set; }
        public MessageType Type { get; set; }
        public List<ServerEvent> EventLog { get; set; }
    }

    public static class MessageExtensions
    {
        public static bool MustBeProcessedImmediately(this Message message)
        {
            switch (message.Type)
            {
                case MessageType.None:
                case MessageType.TextMessage:
                case MessageType.InboundFileTransferRequest:
                case MessageType.OutboundFileTransferRequest:
                case MessageType.RetryOutboundFileTransfer:
                case MessageType.FileListRequest:
                case MessageType.FileListResponse:
                case MessageType.TransferFolderPathRequest:
                case MessageType.TransferFolderPathResponse:
                case MessageType.PublicIpAddressRequest:
                case MessageType.PublicIpAddressResponse:
                case MessageType.NoFilesAvailableForDownload:
                case MessageType.RequestedFolderDoesNotExist:
                    return false;

                case MessageType.FileTransferAccepted:
                case MessageType.FileTransferRejected:
                case MessageType.FileTransferStalled:
                case MessageType.ShutdownServerCommand:
                    return true;
                
                default:
                    return false;
            }
        }
    }
}
