namespace TplSockets
{
    public enum EventType
    {
        None,

        ServerStartedListening,
        ServerStoppedListening,

        ConnectionAccepted,

        ConnectToRemoteServerStarted,
        ConnectToRemoteServerComplete,

        ReceiveMessageFromClientStarted,
        ReceiveMessageFromClientComplete,

        DetermineMessageLengthStarted,
        ReceivedMessageLengthFromSocket,
        DetermineMessageLengthComplete,

        SaveUnreadBytesAfterReceiveMessageLength,
        CopySavedBytesToMessageData,

        ReceiveMessageBytesStarted,
        ReceivedMessageBytesFromSocket,
        ReceiveMessageBytesComplete,

        SaveUnreadBytesAfterReceiveMessage,
        CopySavedBytesToIncomingFile,

        ProcessRequestStarted,
        ProcessRequestComplete,

        SendTextMessageStarted,
        SendTextMessageComplete,
        ReceivedTextMessage,

        RequestOutboundFileTransferStarted,
        RequestOutboundFileTransferComplete,
        ReceivedOutboundFileTransferRequest,

        SendFileBytesStarted,
        SentFileChunkToClient,
        SendFileBytesComplete,

        RequestInboundFileTransferStarted,
        RequestInboundFileTransferComplete,
        ReceivedInboundFileTransferRequest,

        SendFileTransferRejectedStarted,
        SendFileTransferRejectedComplete,
        ReceiveFileTransferRejectedStarted,
        ClientRejectedFileTransfer,

        SendFileTransferAcceptedStarted,
        SendFileTransferAcceptedComplete,
        ReceiveFileTransferAcceptedStarted,
        ClientAcceptedFileTransfer,

        ReceiveFileBytesStarted,
        ReceivedFileBytesFromSocket,
        UpdateFileTransferProgress,
        ReceiveFileBytesComplete,

        SendFileTransferStalledStarted,
        SendFileTransferStalledComplete,
        FileTransferStalled,

        RetryOutboundFileTransferStarted,
        RetryOutboundFileTransferComplete,
        ReceiveRetryOutboundFileTransferStarted,
        ReceivedRetryOutboundFileTransferRequest,

        SendConfirmationMessageStarted,
        SendConfirmationMessageComplete,
        ReceiveConfirmationMessageStarted,
        ReceiveConfirmationMessageComplete,
        
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

        ShutdownListenSocketStarted,
        ShutdownListenSocketCompletedWithoutError,
        ShutdownListenSocketCompletedWithError,
        ShutdownTransferSocketStarted,
        ShutdownTransferSocketComplete,

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
}
