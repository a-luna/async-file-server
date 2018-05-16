namespace TplSockets
{
    public enum MessageType
    {
        None                                  = 0,
        TextMessage                           = 1,
        InboundFileTransfer                   = 2,
        OutboundFileTransfer                  = 3,
        FileListRequest                       = 4,
        FileList                              = 5,
        TransferFolderPathRequest             = 6,
        TransferFolderPath                    = 7,
        PublicIpAddressRequest                = 8,
        PublicIpAddress                       = 9,
        NoFilesAvailableForDownload           = 10,
        FileTransferAccepted                  = 11,
        FileTransferRejected                  = 12,
        FileTransferStalled                   = 13,
        RetryOutboundFileTransfer             = 14,
        RequestedFolderDoesNotExist           = 15,
        ShutdownServerCommand                 = 16
    }

    public static class MessageTypeExtensions
    {
        public static string Name(this MessageType messageType)
        {
            switch (messageType)
            {
                case MessageType.TextMessage:
                    return "RECEIVE TEXT MESSAGE";
                    
                case MessageType.OutboundFileTransfer:
                    return "FILE TRANSFER REQUEST";

                case MessageType.FileTransferAccepted:
                    return "SEND FILE";

                case MessageType.InboundFileTransfer:
                    return "RECEIVE FILE";

                case MessageType.FileTransferRejected:
                    return "SERVER RESPONSE: FILE TRANSFER REJECTED";

                case MessageType.FileTransferStalled:
                    return "SERVER RESPONSE: FILE TRANSFER STALLED";

                case MessageType.RetryOutboundFileTransfer:
                    return "RETRY STALLED FILE TRANSFER";

                case MessageType.FileListRequest:
                    return "FILE LIST REQUEST";

                case MessageType.FileList:
                    return "RECEIVE FILE LIST";

                case MessageType.TransferFolderPathRequest:
                    return "TRANSFER FOLDER PATH REQUEST";

                case MessageType.TransferFolderPath:
                    return "RECEIVE TRANSFER FOLDER PATH";

                case MessageType.NoFilesAvailableForDownload:
                    return "SERVER RESPONSE: REQUESTED FOLDER IS EMPTY";

                case MessageType.RequestedFolderDoesNotExist:
                    return "SERVER RESPONSE: REQUESTED FOLDER DOES NOT EXIST";

                case MessageType.PublicIpAddressRequest:
                    return "PUBLIC IP ADDRESS REQUEST";

                case MessageType.PublicIpAddress:
                    return "RECEIVE PUBLIC IP ADDRESS";

                case MessageType.ShutdownServerCommand:
                    return "SHUTDOWN SERVER";
            }

            return string.Empty;
        }
    }
}
