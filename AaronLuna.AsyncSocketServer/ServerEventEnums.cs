namespace AaronLuna.AsyncSocketServer
{
    public enum LogLevel
    {
        None,
        Info,
        Debug,
        Trace
    }

    public enum EventType
    {
        None,

        ServerStartedListening,
        ServerStoppedListening,

        ConnectionAccepted,

        ConnectToRemoteServerStarted,
        ConnectToRemoteServerComplete,

        ReceiveRequestFromRemoteServerStarted,
        ReceiveRequestFromRemoteServerComplete,

        ReceiveRequestLengthStarted,
        ReceivedRequestLengthBytesFromSocket,
        ReceiveRequestLengthComplete,

        SaveUnreadBytesAfterRequestLengthReceived,
        CopySavedBytesToRequestData,

        ReceiveRequestBytesStarted,
        ReceivedRequestBytesFromSocket,
        ReceiveRequestBytesComplete,

        SaveUnreadBytesAfterAllRequestBytesReceived,
        CopySavedBytesToIncomingFile,

        DetermineRequestTypeStarted,
        DetermineRequestTypeComplete,

        PendingRequestInQueue,
        ProcessRequestBacklogStarted,
        ProcessRequestBacklogComplete,
        PendingFileTransfer,
        ProcessRequestStarted,
        ProcessRequestComplete,

        SendTextMessageStarted,
        SendTextMessageComplete,
        ReceivedTextMessage,
        MarkTextMessageAsRead,

        FileTransferStatusChange,

        RequestOutboundFileTransferStarted,
        RequestOutboundFileTransferComplete,
        ReceivedOutboundFileTransferRequest,

        SendFileBytesStarted,
        SentFileChunkToRemoteServer,
        SendFileBytesComplete,

        MultipleFileWriteAttemptsNeeded,

        RequestInboundFileTransferStarted,
        RequestInboundFileTransferComplete,
        ReceivedInboundFileTransferRequest,

        SendFileTransferRejectedStarted,
        SendFileTransferRejectedComplete,
        RemoteServerRejectedFileTransfer,

        SendFileTransferAcceptedStarted,
        SendFileTransferAcceptedComplete,
        RemoteServerAcceptedFileTransfer,

        SendFileTransferCompletedStarted,
        SendFileTransferCompletedComplete,
        RemoteServerConfirmedFileTransferCompleted,

        ReceiveFileBytesStarted,
        ReceivedFileBytesFromSocket,
        UpdateFileTransferProgress,
        ReceiveFileBytesComplete,

        SendFileTransferStalledStarted,
        SendFileTransferStalledComplete,
        FileTransferStalled,

        RetryOutboundFileTransferStarted,
        RetryOutboundFileTransferComplete,
        ReceivedRetryOutboundFileTransferRequest,

        SendRetryLimitExceededStarted,
        SendRetryLimitExceededCompleted,
        ReceivedRetryLimitExceeded,
        RetryLimitLockoutExpired,

        RequestFileListStarted,
        RequestFileListComplete,
        ReceivedFileListRequest,

        SendFileListStarted,
        SendFileListComplete,
        ReceivedFileList,

        SendNotificationFolderIsEmptyStarted,
        SendNotificationFolderIsEmptyComplete,
        ReceivedNotificationFolderIsEmpty,

        SendNotificationFolderDoesNotExistStarted,
        SendNotificationFolderDoesNotExistComplete,
        ReceivedNotificationFolderDoesNotExist,

        SendNotificationFileDoesNotExistStarted,
        SendNotificationFileDoesNotExistComplete,
        ReceivedNotificationFileDoesNotExist,

        ShutdownListenSocketStarted,
        ShutdownListenSocketCompletedWithoutError,
        ShutdownListenSocketCompletedWithError,

        SendShutdownServerCommandStarted,
        SendShutdownServerCommandComplete,
        ReceivedShutdownServerCommand,

        RequestServerInfoStarted,
        RequestServerInfoComplete,
        ReceivedServerInfoRequest,

        SendServerInfoStarted,
        SendServerInfoComplete,
        ReceivedServerInfo,

        ErrorOccurred,

        StoppedSendingFileBytes
    }

    public static class ServerEventTypeExtensions
    {
        public static bool DoNotDisplayInLog(this EventType eventType)
        {
            switch (eventType)
            {
                case EventType.ProcessRequestStarted:
                case EventType.ProcessRequestComplete:
                case EventType.ConnectToRemoteServerStarted:
                case EventType.ConnectToRemoteServerComplete:
                    return true;

                default:
                    return false;
            }
        }

        public static bool LogLevelIsTraceOnly(this EventType eventType)
        {
            switch (eventType)
            {
                case EventType.SentFileChunkToRemoteServer:
                case EventType.UpdateFileTransferProgress:
                    return true;

                default:
                    return false;
            }
        }

        public static bool LogLevelIsDebugOnly(this EventType eventType)
        {
            switch (eventType)
            {
                case EventType.ConnectToRemoteServerStarted:
                case EventType.ConnectToRemoteServerComplete:
                case EventType.ReceiveRequestFromRemoteServerStarted:
                case EventType.ReceiveRequestFromRemoteServerComplete:
                case EventType.ReceiveRequestLengthStarted:
                case EventType.ReceivedRequestLengthBytesFromSocket:
                case EventType.SaveUnreadBytesAfterRequestLengthReceived:
                case EventType.ReceiveRequestLengthComplete:
                case EventType.ReceiveRequestBytesStarted:
                case EventType.CopySavedBytesToRequestData:
                case EventType.DetermineRequestTypeStarted:
                case EventType.DetermineRequestTypeComplete:
                case EventType.ReceivedRequestBytesFromSocket:
                case EventType.ReceiveRequestBytesComplete:
                case EventType.SaveUnreadBytesAfterAllRequestBytesReceived:
                case EventType.ProcessRequestStarted:
                case EventType.ProcessRequestComplete:
                case EventType.SentFileChunkToRemoteServer:
                case EventType.CopySavedBytesToIncomingFile:
                case EventType.ReceivedFileBytesFromSocket:
                case EventType.MultipleFileWriteAttemptsNeeded:
                case EventType.UpdateFileTransferProgress:
                    return true;

                default:
                    return false;
            }
        }
    }
}
