namespace AaronLuna.AsyncFileServer.Model
{
    public enum ServerRequestType
    {
        None                                  = 0,
        ServerInfoRequest                     = 1,
        ServerInfoResponse                    = 2,
        TextMessage                           = 3,
        InboundFileTransferRequest            = 4,
        OutboundFileTransferRequest           = 5,
        FileListRequest                       = 6,
        FileListResponse                      = 7,
        NoFilesAvailableForDownload           = 8,
        RequestedFolderDoesNotExist           = 9,
        RequestedFileDoesNotExist             = 10,
        FileTransferAccepted                  = 11,
        FileTransferRejected                  = 12,
        FileTransferStalled                   = 13,
        FileTransferComplete                  = 14,
        RetryOutboundFileTransfer             = 15,
        RetryLimitExceeded                    = 16,
        RetryLockoutExpired                   = 17,
        ShutdownServerCommand                 = 18
    }

    public static class RequestTypeExtensions
    {
        public static string Name(this ServerRequestType messageType)
        {
            switch (messageType)
            {
                case ServerRequestType.ServerInfoRequest:
                    return "SERVER INFO REQUEST";

                case ServerRequestType.ServerInfoResponse:
                    return "SERVER INFO RESPONSE";

                case ServerRequestType.TextMessage:
                    return "TEXT MESSAGE";

                case ServerRequestType.OutboundFileTransferRequest:
                    return "OUTBOUND FILE TRANSFER REQUEST";

                case ServerRequestType.FileTransferAccepted:
                    return "FILE TRANSFER ACCEPTED";

                case ServerRequestType.InboundFileTransferRequest:
                    return "INBOUND FILE TRANSFER REQUEST";

                case ServerRequestType.FileTransferRejected:
                    return "FILE TRANSFER REJECTED";

                case ServerRequestType.FileTransferStalled:
                    return "FILE TRANSFER STALLED";

                case ServerRequestType.RetryOutboundFileTransfer:
                    return "RETRY STALLED FILE TRANSFER";

                case ServerRequestType.FileListRequest:
                    return "FILE LIST REQUEST";

                case ServerRequestType.FileListResponse:
                    return "FILE LIST RESPONSE";

                case ServerRequestType.NoFilesAvailableForDownload:
                    return "REQUESTED FOLDER IS EMPTY";

                case ServerRequestType.RequestedFolderDoesNotExist:
                    return "REQUESTED FOLDER DOES NOT EXIST";
                    
                case ServerRequestType.ShutdownServerCommand:
                    return "SHUTDOWN SERVER";
                    
                case ServerRequestType.FileTransferComplete:
                    return "FILE TRANSFER COMPLETE";

                case ServerRequestType.RetryLimitExceeded:
                    return "RETRY LIMIT EXCEEDED";

                case ServerRequestType.RetryLockoutExpired:
                    return "RETRY LOCKOUT EXPIRED";

                case ServerRequestType.RequestedFileDoesNotExist:
                    return "REQUESTED FILE DOES NOT EXIST";

                default:
                    return string.Empty;
            }
        }

        public static bool ProcessRequestImmediately(this ServerRequestType responseType)
        {
            switch (responseType)
            {
                case ServerRequestType.None:
                case ServerRequestType.TextMessage:
                case ServerRequestType.InboundFileTransferRequest:
                    return false;

                case ServerRequestType.ServerInfoRequest:
                case ServerRequestType.ServerInfoResponse:
                case ServerRequestType.FileListRequest:
                case ServerRequestType.FileListResponse:
                case ServerRequestType.OutboundFileTransferRequest:
                case ServerRequestType.NoFilesAvailableForDownload:
                case ServerRequestType.RequestedFolderDoesNotExist:
                case ServerRequestType.FileTransferAccepted:
                case ServerRequestType.FileTransferRejected:
                case ServerRequestType.FileTransferStalled:
                case ServerRequestType.RetryOutboundFileTransfer:
                case ServerRequestType.FileTransferComplete:
                case ServerRequestType.RetryLimitExceeded:
                case ServerRequestType.RetryLockoutExpired:
                case ServerRequestType.RequestedFileDoesNotExist:
                case ServerRequestType.ShutdownServerCommand:
                    return true;
                    
                default:
                    return false;
            }
        }

        public static bool IsFileTransferResponse(this ServerRequestType responseType)
        {
            switch (responseType)
            {
                case ServerRequestType.FileTransferAccepted:
                case ServerRequestType.FileTransferRejected:
                case ServerRequestType.FileTransferStalled:
                case ServerRequestType.FileTransferComplete:
                case ServerRequestType.RetryOutboundFileTransfer:
                case ServerRequestType.RequestedFileDoesNotExist:
                    return true;

                default:
                    return false;
            }
        }

        public static bool IsFileTransferError(this ServerRequestType responseType)
        {
            switch (responseType)
            {
                case ServerRequestType.RetryLimitExceeded:
                case ServerRequestType.RequestedFileDoesNotExist:
                    return true;

                default:
                    return false;
            }
        }
    }
}
