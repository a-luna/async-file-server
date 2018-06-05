using System;

namespace TplSockets
{
    public enum RequestType
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
        ShutdownServerCommand                 = 17
    }

    public static class RequestTypeExtensions
    {
        public static string Name(this RequestType messageType)
        {
            switch (messageType)
            {
                case RequestType.ServerInfoRequest:
                    return "SERVER INFO REQUEST";

                case RequestType.ServerInfoResponse:
                    return "SERVER INFO RESPONSE";

                case RequestType.TextMessage:
                    return "TEXT MESSAGE";

                case RequestType.OutboundFileTransferRequest:
                    return "OUTBOUND FILE TRANSFER REQUEST";

                case RequestType.FileTransferAccepted:
                    return "FILE TRANSFER ACCEPTED";

                case RequestType.InboundFileTransferRequest:
                    return "INBOUND FILE TRANSFER REQUEST";

                case RequestType.FileTransferRejected:
                    return "FILE TRANSFER REJECTED";

                case RequestType.FileTransferStalled:
                    return "FILE TRANSFER STALLED";

                case RequestType.RetryOutboundFileTransfer:
                    return "RETRY STALLED FILE TRANSFER";

                case RequestType.FileListRequest:
                    return "FILE LIST REQUEST";

                case RequestType.FileListResponse:
                    return "FILE LIST RESPONSE";

                case RequestType.NoFilesAvailableForDownload:
                    return "REQUESTED FOLDER IS EMPTY";

                case RequestType.RequestedFolderDoesNotExist:
                    return "REQUESTED FOLDER DOES NOT EXIST";
                    
                case RequestType.ShutdownServerCommand:
                    return "SHUTDOWN SERVER";
                    
                case RequestType.FileTransferComplete:
                    return "FILE TRANSFER COMPLETE";

                case RequestType.RetryLimitExceeded:
                    return "RETRY LIMIT EXCEEDED";

                case RequestType.RequestedFileDoesNotExist:
                    return "REQUESTED FILE DOES NOT EXIST";

                default:
                    return string.Empty;
            }
        }

        public static bool ProcessRequestImmediately(this RequestType messageType)
        {
            switch (messageType)
            {
                case RequestType.None:
                case RequestType.TextMessage:
                case RequestType.InboundFileTransferRequest:
                    return false;

                case RequestType.ServerInfoRequest:
                case RequestType.ServerInfoResponse:
                case RequestType.FileListRequest:
                case RequestType.FileListResponse:
                case RequestType.OutboundFileTransferRequest:
                case RequestType.NoFilesAvailableForDownload:
                case RequestType.RequestedFolderDoesNotExist:
                case RequestType.FileTransferAccepted:
                case RequestType.FileTransferRejected:
                case RequestType.FileTransferStalled:
                case RequestType.RetryOutboundFileTransfer:
                case RequestType.FileTransferComplete:
                case RequestType.RetryLimitExceeded:
                case RequestType.RequestedFileDoesNotExist:
                case RequestType.ShutdownServerCommand:
                    return true;
                    
                default:
                    return false;
            }
        }
    }
}
