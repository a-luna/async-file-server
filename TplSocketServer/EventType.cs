namespace TplSockets
{
    public enum EventType
    {
        None,

        ServerIsListening,
        EnterMainLoop,
        ExitMainLoop,

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

        ProcessUnknownHostStarted,
        ProcessUnkownHostComplete,

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

        //SendFileTransferCanceledStarted,
        //SendFileTransferCanceledComplete,
        //ReceiveFileTransferCanceledStarted,
        //ReceiveFileTransferCanceledComplete,

        RetryOutboundFileTransferStarted,
        RetryOutboundFileTransferComplete,
        ReceiveRetryOutboundFileTransferStarted,
        ReceivedRetryOutboundFileTransferRequest,

        SendConfirmationMessageStarted,
        SendConfirmationMessageComplete,
        ReceiveConfirmationMessageStarted,
        ReceiveConfirmationMessageComplete,

        RequestPublicIpAddressStarted,
        RequestPublicIpAddressComplete,
        ReceivedPublicIpAddressRequest,

        SendPublicIpAddressStarted,
        SendPublicIpAddressComplete,
        ReceivedPublicIpAddress,

        RequestTransferFolderPathStarted,
        RequestTransferFolderPathComplete,
        ReceivedTransferFolderPathRequest,

        SendTransferFolderPathStarted,
        SendTransferFolderPathComplete,
        ReceivedTransferFolderPath,

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

        ErrorOccurred
    }
}
