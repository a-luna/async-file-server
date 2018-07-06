namespace AaronLuna.AsyncFileServer.Model
{
    public enum ServerEventType
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

        QueueContainsUnhandledRequests,
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
        SentFileChunkToClient,
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
        SendFileTransferCompletedCompleted,
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

        RequestFileListStarted,
        RequestFileListComplete,
        ReceivedFileListRequest,

        SendFileListStarted,
        SendFileListComplete,
        ReceivedFileList,

        SendNotificationNoFilesToDownloadStarted,
        SendNotificationNoFilesToDownloadComplete,
        ReceivedNotificationNoFilesToDownload,

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

        ErrorOccurred
    }

    public static class ServerEventTypeExtensions
    {
        public static bool ExcludeFromEventLog(this ServerEventType eventType)
        {
            switch (eventType)
            {
                case ServerEventType.ConnectToRemoteServerStarted:
                case ServerEventType.ConnectToRemoteServerComplete:
                case ServerEventType.ReceiveRequestFromRemoteServerStarted:
                case ServerEventType.ReceiveRequestFromRemoteServerComplete:
                case ServerEventType.ReceiveRequestLengthStarted:
                case ServerEventType.ReceivedRequestLengthBytesFromSocket:
                case ServerEventType.SaveUnreadBytesAfterRequestLengthReceived:
                case ServerEventType.ReceiveRequestLengthComplete:
                case ServerEventType.ReceiveRequestBytesStarted:
                case ServerEventType.CopySavedBytesToRequestData:
                case ServerEventType.ReceivedRequestBytesFromSocket:
                case ServerEventType.ReceiveRequestBytesComplete:
                case ServerEventType.SaveUnreadBytesAfterAllRequestBytesReceived:
                case ServerEventType.ProcessRequestStarted:
                case ServerEventType.ProcessRequestComplete:
                case ServerEventType.SentFileChunkToClient:
                case ServerEventType.CopySavedBytesToIncomingFile:
                case ServerEventType.ReceivedFileBytesFromSocket:
                case ServerEventType.UpdateFileTransferProgress:
                    return true;

                default:
                    return false;
            }
        }
    }
}
