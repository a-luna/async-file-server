namespace TplSockets
{
    public enum MessageType
    {
        None                                  = 0,
        TextMessage                           = 1,
        InboundFileTransferRequest            = 2,
        OutboundFileTransferRequest           = 3,
        FileListRequest                       = 4,
        FileListResponse                      = 5,
        NoFilesAvailableForDownload           = 6,
        FileTransferAccepted                  = 7,
        FileTransferRejected                  = 8,
        FileTransferStalled                   = 9,
        RetryOutboundFileTransfer             = 10,
        RequestedFolderDoesNotExist           = 11,
        ServerInfoRequest                     = 12,
        ServerInfoResponse                    = 13,
        ShutdownServerCommand                 = 14
    }

    public static class MessageTypeExtensions
    {
        public static string Name(this MessageType messageType)
        {
            switch (messageType)
            {
                case MessageType.ServerInfoRequest:
                    return "SERVER INFO REQUEST";

                case MessageType.ServerInfoResponse:
                    return "SERVER INFO RESPONSE";

                case MessageType.TextMessage:
                    return "TEXT MESSAGE";

                case MessageType.OutboundFileTransferRequest:
                    return "OUTBOUND FILE TRANSFER REQUEST";

                case MessageType.FileTransferAccepted:
                    return "FILE TRANSFER ACCEPTED";

                case MessageType.InboundFileTransferRequest:
                    return "INBOUND FILE TRANSFER REQUEST";

                case MessageType.FileTransferRejected:
                    return "FILE TRANSFER REJECTED";

                case MessageType.FileTransferStalled:
                    return "FILE TRANSFER STALLED";

                case MessageType.RetryOutboundFileTransfer:
                    return "RETRY STALLED FILE TRANSFER";

                case MessageType.FileListRequest:
                    return "FILE LIST REQUEST";

                case MessageType.FileListResponse:
                    return "FILE LIST RESPONSE";

                case MessageType.NoFilesAvailableForDownload:
                    return "REQUESTED FOLDER IS EMPTY";

                case MessageType.RequestedFolderDoesNotExist:
                    return "REQUESTED FOLDER DOES NOT EXIST";
                    
                case MessageType.ShutdownServerCommand:
                    return "SHUTDOWN SERVER";
            }

            return string.Empty;
        }
    }
}
