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
        //FileTransferCanceled                  = 14,
        RetryOutboundFileTransfer             = 15,
        RequestedFolderDoesNotExist           = 16,
        ShutdownServerCommand                 = 17
    }

    public static class MessageTypeExtensions
    {
        public static string Name(this MessageType messageType)
        {
            switch (messageType)
            {
                case MessageType.TextMessage:
                    return "RECEIVE TEXT MESSAGE";

                case MessageType.InboundFileTransfer:
                    return "GET FILE";

                case MessageType.OutboundFileTransfer:
                    return "FILE TRANSFER REQUEST";

                case MessageType.FileListRequest:
                    return "FILE LIST REQUEST";

                case MessageType.FileList:
                    return "RECEIVE FILE LIST";

                case MessageType.TransferFolderPathRequest:
                    return "TRANSFER FOLDER PATH REQUEST";

                case MessageType.TransferFolderPath:
                    return "RECEIVE TRANSFER FOLDER PATH";

                case MessageType.PublicIpAddressRequest:
                    return "PUBLIC IP ADDRESS REQUEST";

                case MessageType.PublicIpAddress:
                    return "RECEIVE PUBLIC IP ADDRESS";

                case MessageType.NoFilesAvailableForDownload:
                    return "REQUESTED FOLDER IS EMPTY";

                case MessageType.FileTransferAccepted:
                    return "SEND FILE";

                case MessageType.FileTransferRejected:
                    return "FILE TRANSFER REJECTED";

                case MessageType.FileTransferStalled:
                    return "FILE TRANSFER STALLED";

                case MessageType.RetryOutboundFileTransfer:
                    return "RETRY STALLED FILE TRANSFER";

                case MessageType.RequestedFolderDoesNotExist:
                    return "REQUESTED FOLDER DOES NOT EXIST";

                case MessageType.ShutdownServerCommand:
                    return "SHUTDOWN SERVER";
            }

            return string.Empty;
        }
    }
}
