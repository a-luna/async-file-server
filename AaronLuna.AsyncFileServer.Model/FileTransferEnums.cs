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
        AwaitingResponse,
        Accepted,
        Rejected,
        InProgress,
        Stalled,
        Cancelled,
        AwaitingConfirmation,
        Complete,
        RetryLimitExceeded,
        Error
    }

    public enum FileTransferLogLevel
    {
        None,
        Normal,
        Debug
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
        public static string ToString(this FileTransferStatus status)
        {
            switch (status)
            {
                case FileTransferStatus.AwaitingResponse:
                    return "Awaiting Response";

                case FileTransferStatus.InProgress:
                    return "In Progress";

                case FileTransferStatus.AwaitingConfirmation:
                    return "Awaiting Confirmation";

                case FileTransferStatus.RetryLimitExceeded:
                    return "Retry Limit Exceeded";

                case FileTransferStatus.Accepted:
                case FileTransferStatus.Rejected:
                case FileTransferStatus.Stalled:
                case FileTransferStatus.Cancelled:
                case FileTransferStatus.Complete:
                case FileTransferStatus.Error:
                    return status.ToString();

                default:
                    return "N/A";
            }
        }

        public static bool TasksRemaining(this FileTransferStatus status)
        {
            switch (status)
            {
                case FileTransferStatus.AwaitingResponse:
                case FileTransferStatus.Accepted:
                case FileTransferStatus.InProgress:
                case FileTransferStatus.AwaitingConfirmation:
                    return true;

                default:
                    return false;
            }
        }
    }
}
