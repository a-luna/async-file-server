using System;

namespace AaronLuna.AsyncFileServer.Model
{
    public enum ServerRequestDirection
    {
        None,
        Sent,
        Received
    }

    public enum ServerRequestStatus
    {
        NoData,
        Pending,
        InProgress,
        Processed,
        Sent,
        Error
    }

    public enum ServerRequestType : byte
    {
        None                              = 0,

        ServerInfoRequest                 = 10,
        ServerInfoResponse                = 11,

        TextMessage                       = 20,

        FileListRequest                   = 30,
        FileListResponse                  = 31,
        NoFilesAvailableForDownload       = 32,
        RequestedFolderDoesNotExist       = 33,

        InboundFileTransferRequest        = 40,
        RequestedFileDoesNotExist         = 41,

        OutboundFileTransferRequest       = 50,
        FileTransferAccepted              = 51,
        FileTransferRejected              = 52,
        FileTransferStalled               = 53,
        FileTransferComplete              = 54,

        RetryOutboundFileTransfer         = 60,
        RetryLimitExceeded                = 62,
        RetryLockoutExpired               = 63,

        ShutdownServerCommand             = 255
    }

    public static class RequestStatusExtensions
    {
        public static bool RequestHasBeenProcesed(this ServerRequestStatus status)
        {
            switch (status)
            {
                case ServerRequestStatus.NoData:
                case ServerRequestStatus.Pending:
                case ServerRequestStatus.InProgress:
                    return false;

                case ServerRequestStatus.Processed:
                case ServerRequestStatus.Sent:
                case ServerRequestStatus.Error:
                    return true;

                default:
                    throw new ArgumentOutOfRangeException(nameof(status), status, null);
            }
        }
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

        public static bool IsLongRunningProcess(this ServerRequestType requestType)
        {
            switch (requestType)
            {
                case ServerRequestType.FileTransferAccepted:
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
