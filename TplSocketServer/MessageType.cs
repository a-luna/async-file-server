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
        TransferFolderPathRequest             = 6,
        TransferFolderPathResponse            = 7,
        PublicIpAddressRequest                = 8,
        PublicIpAddressResponse               = 9,
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
                    return "TEXT MESSAGE";

                case MessageType.OutboundFileTransferRequest:
                    return "FILE TRANSFER REQUEST";

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

                case MessageType.TransferFolderPathRequest:
                    return "TRANSFER FOLDER PATH REQUEST";

                case MessageType.TransferFolderPathResponse:
                    return "TRANSFER FOLDER PATH RESPONSE";

                case MessageType.NoFilesAvailableForDownload:
                    return "REQUESTED FOLDER IS EMPTY";

                case MessageType.RequestedFolderDoesNotExist:
                    return "REQUESTED FOLDER DOES NOT EXIST";

                case MessageType.PublicIpAddressRequest:
                    return "PUBLIC IP ADDRESS REQUEST";

                case MessageType.PublicIpAddressResponse:
                    return "PUBLIC IP ADDRESS RESPONSE";

                case MessageType.ShutdownServerCommand:
                    return "SHUTDOWN SERVER";
            }

            return string.Empty;
        }
    }
}
