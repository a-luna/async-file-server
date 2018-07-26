namespace AaronLuna.AsyncFileServer.Model
{
    public enum FileTransferDirection
    {
        None,
        Inbound,
        Outbound
    }

    public enum FileTransferInitiator
    {
        None,
        Self,
        RemoteServer
    }

    public enum FileTransferStatus
    {
        None,
        Pending,
        Accepted,
        Rejected,
        InProgress,
        Stalled,
        Cancelled,
        TransferComplete,
        ConfirmedComplete,
        RetryLimitExceeded,
        Error
    }

    public static class FileTransferInitiatorExtensions
    {
        public static string ToString(this FileTransferInitiator initiator)
        {
            switch (initiator)
            {
                case FileTransferInitiator.RemoteServer:
                    return "Remote Server";

                case FileTransferInitiator.Self:
                    return initiator.ToString();

                default:
                    return "N/A";
            }
        }
    }

    public static class FileTransferStatusExtensions
    {
        public static string Name(this FileTransferStatus status)
        {
            switch (status)
            {
                case FileTransferStatus.Pending:
                    return "PENDING";

                case FileTransferStatus.InProgress:
                    return "In Progress";

                case FileTransferStatus.TransferComplete:
                    return "Transfer Complete";

                case FileTransferStatus.RetryLimitExceeded:
                    return "Retry Limit Exceeded";

                case FileTransferStatus.ConfirmedComplete:
                    return "Complete";

                case FileTransferStatus.Accepted:
                case FileTransferStatus.Rejected:
                case FileTransferStatus.Stalled:
                case FileTransferStatus.Cancelled:                
                case FileTransferStatus.Error:
                    return status.ToString();

                default:
                    return "N/A";
            }
        }

        public static bool TransferNeverStarted(this FileTransferStatus status)
        {
            switch (status)
            {
                case FileTransferStatus.Pending:
                case FileTransferStatus.Accepted:
                case FileTransferStatus.Rejected:
                case FileTransferStatus.RetryLimitExceeded:
                case FileTransferStatus.Error:
                    return true;

                default:
                    return false;
            }
        }

        public static bool TransferStartedButDidNotComplete(this FileTransferStatus status)
        {
            switch (status)
            {
                case FileTransferStatus.InProgress:
                case FileTransferStatus.Stalled:
                case FileTransferStatus.Cancelled:
                    return true;

                default:
                    return false;
            }
        }

        public static bool TransfercompletedSucessfully(this FileTransferStatus status)
        {
            switch (status)
            {
                case FileTransferStatus.TransferComplete:
                case FileTransferStatus.ConfirmedComplete:
                    return true;

                default:
                    return false;
            }
        }
    }
}
