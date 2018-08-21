using System;

namespace AaronLuna.AsyncSocketServer.Requests
{
    public enum RequestType : byte
    {
        None                        = 0,

        ServerInfoRequest           = 10,
        ServerInfoResponse          = 11,

        MessageRequest                 = 20,

        FileListRequest             = 30,
        FileListResponse            = 31,
        RequestedFolderIsEmpty      = 32,
        RequestedFolderDoesNotExist = 33,

        InboundFileTransferRequest  = 40,
        RequestedFileDoesNotExist   = 41,

        OutboundFileTransferRequest = 50,
        FileTransferAccepted        = 51,
        FileTransferRejected        = 52,
        FileTransferStalled         = 53,
        FileTransferComplete        = 54,

        RetryOutboundFileTransfer   = 60,
        RetryLimitExceeded          = 62,

        ShutdownServerCommand       = 255
    }

    public enum RequestStatus
    {
        NoData,
        Pending,
        InProgress,
        Processed,
        Sent,
        Error
    }

    public static class RequestStatusExtensions
    {
        public static bool RequestHasBeenProcesed(this RequestStatus status)
        {
            switch (status)
            {
                case RequestStatus.NoData:
                case RequestStatus.Pending:
                case RequestStatus.InProgress:
                    return false;

                case RequestStatus.Processed:
                case RequestStatus.Sent:
                case RequestStatus.Error:
                    return true;

                default:
                    throw new ArgumentOutOfRangeException(nameof(status), status, null);
            }
        }
    }

    public static class RequestTypeExtensions
    {
        public static string Name(this RequestType messageType)
        {
            switch (messageType)
            {
                case RequestType.None:
                    return "NONE";

                case RequestType.ServerInfoRequest:
                    return "SERVER INFO REQUEST";

                case RequestType.ServerInfoResponse:
                    return "SERVER INFO RESPONSE";

                case RequestType.MessageRequest:
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

                case RequestType.RequestedFolderIsEmpty:
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

        public static bool IsLongRunningProcess(this RequestType requestType)
        {
            switch (requestType)
            {
                case RequestType.FileTransferAccepted:
                    return true;

                default:
                    return false;
            }
        }

        public static bool IsFileTransferResponse(this RequestType responseType)
        {
            switch (responseType)
            {
                case RequestType.FileTransferAccepted:
                case RequestType.FileTransferRejected:
                case RequestType.FileTransferStalled:
                case RequestType.FileTransferComplete:
                case RequestType.RetryOutboundFileTransfer:
                case RequestType.RequestedFileDoesNotExist:
                    return true;

                default:
                    return false;
            }
        }

        public static bool IsFileTransferError(this RequestType responseType)
        {
            switch (responseType)
            {
                case RequestType.RetryLimitExceeded:
                case RequestType.RequestedFileDoesNotExist:
                    return true;

                default:
                    return false;
            }
        }
    }
}
