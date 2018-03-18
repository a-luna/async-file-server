namespace TplSocketServer
{
    public enum EventType
    {
        None,
        
        ListenOnLocalPortStarted,
        ListenOnLocalPortComplete,

        EnterMainAcceptConnectionLoop,
        ExitMainAcceptConnectionLoop,

        AcceptConnectionAttemptStarted,
        AcceptConnectionAttemptComplete,

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

        DetermineMessageTypeStarted,
        DetermineMessageTypeComplete,

        SendTextMessageStarted,
        SendTextMessageComplete,
        ReadTextMessageStarted,
        ReadTextMessageComplete,

        SendOutboundFileTransferInfoStarted,
        SendOutboundFileTransferInfoComplete,
        ReadOutboundFileTransferInfoStarted,
        ReadOutboundFileTransferInfoComplete,

        SendFileBytesStarted,
        SentFileChunkToClient,
        SendFileBytesComplete,

        SendInboundFileTransferInfoStarted,
        SendInboundFileTransferInfoComplete,
        ReadInboundFileTransferInfoStarted,
        ReadInboundFileTransferInfoComplete,

        SendFileTransferRejectedStarted,
        SendFileTransferRejectedComplete,
        ReceiveFileTransferRejectedStarted,
        ReceiveFileTransferRejectedComplete,

        SendFileTransferAcceptedStarted,
        SendFileTransferAcceptedComplete,
        ReceiveFileTransferAcceptedStarted,
        ReceiveFileTransferAcceptedComplete,

        ReceiveFileBytesStarted,
        ReceivedFileBytesFromSocket,
        UpdateFileTransferProgress,
        ReceiveFileBytesComplete,

        SendFileTransferStalledStarted,
        SendFileTransferStalledComplete,
        ReceiveFileTransferStalledStarted,
        ReceiveFileTransferStalledComplete,

        SendFileTransferCanceledStarted,
        SendFileTransferCanceledComplete,
        ReceiveFileTransferCanceledStarted,
        ReceiveFileTransferCanceledComplete,

        SendRetryOutboundFileTransferStarted,
        SendRetryOutboundFileTransferComplete,
        ReceiveRetryOutboundFileTransferStarted,
        ReceiveRetryOutboundFileTransferComplete,

        SendConfirmationMessageStarted,
        SendConfirmationMessageComplete,
        ReceiveConfirmationMessageStarted,
        ReceiveConfirmationMessageComplete,

        SendPublicIpRequestStarted,
        SendPublicIpRequestComplete,
        ReadPublicIpRequestStarted,
        ReadPublicIpRequestComplete,

        SendPublicIpResponseStarted,
        SendPublicIpResponseComplete,
        ReadPublicIpResponseStarted,
        ReadPublicIpResponseComplete,

        SendTransferFolderRequestStarted,
        SendTransferFolderRequestComplete,
        ReadTransferFolderRequestStarted,
        ReadTransferFolderRequestComplete,

        SendTransferFolderResponseStarted,
        SendTransferFolderResponseComplete,
        ReadTransferFolderResponseStarted,
        ReadTransferFolderResponseComplete,

        SendFileListRequestStarted,
        SendFileListRequestComplete,
        ReadFileListRequestStarted,
        ReadFileListRequestComplete,

        SendFileListResponseStarted,
        SendFileListResponseComplete,
        ReadFileListResponseStarted,
        ReadFileListResponseComplete,

        SendNotificationNoFilesToDownloadStarted,
        SendNotificationNoFilesToDownloadComplete,
        ReceiveNotificationNoFilesToDownloadStarted,
        ReceiveNotificationNoFilesToDownloadComplete,

        SendNotificationFolderDoesNotExistStarted,
        SendNotificationFolderDoesNotExistComplete,
        ReceiveNotificationFolderDoesNotExistStarted,
        ReceiveNotificationFolderDoesNotExistComplete,

        ShutdownListenSocketStarted,
        ShutdownListenSocketCompletedWithoutError,
        ShutdownListenSocketCompletedWithError,
        ShutdownTransferSocketStarted,
        ShutdownTransferSocketComplete,

        SendShutdownServerCommandStarted,
        SendShutdownServerCommandComplete,
        ReceiveShutdownServerCommandStarted,
        ReceiveShutdownServerCommandComplete,

        ErrorOccurred
    }
}
