namespace TplSocketServer
{
    public enum ServerEventType
    {
        None,
        ListenOnLocalPortStarted,
        ListenOnLocalPortCompleted,

        AcceptConnectionAttemptStarted,
        AcceptConnectionAttemptCompleted,

        ConnectToRemoteServerStarted,
        ConnectToRemoteServerCompleted,

        DetermineMessageLengthStarted,
        DetermineMessageLengthCompleted,

        ReceiveAllMessageBytesStarted,
        ReceiveAllMessageBytesCompleted,

        AppendUnreadBytesToMessageData,

        DetermineRequestTypeStarted,
        DetermineRequestTypeCompleted,

        SendTextMessageStarted,
        SendTextMessageCompleted,
        ReadTextMessageStarted,
        ReadTextMessageCompleted,

        SendInboundFileTransferInfoStarted,
        SendInboundFileTransferInfoCompleted,
        ReadInboundFileTransferInfoStarted,
        ReadInboundFileTransferInfoCompleted,

        SendOutboundFileTransferInfoStarted,
        SendOutboundFileTransferInfoCompleted,
        ReadOutboundFileTransferInfoStarted,
        ReadOutboundFileTransferInfoCompleted,

        SendTransferFolderRequestStarted,
        SendTransferFolderRequestCompleted,
        ReadTransferFolderRequestStarted,
        ReadTransferFolderRequestCompleted,
        SendTransferFolderResponseStarted,
        SendTransferFolderResponseCompleted,
        ReadTransferFolderResponseStarted,
        ReadTransferFolderResponseCompleted,

        SendPublicIpRequestStarted,
        SendPublicIpRequestCompleted,
        ReadPublicIpRequestStarted,
        ReadPublicIpRequestCompleted,
        SendPublicIpResponseStarted,
        SendPublicIpResponseCompleted,
        ReadPublicIpResponseStarted,
        ReadPublicIpResponseCompleted,

        ReadFileListRequestStarted,
        ReadFileListRequestCompleted,
        SendFileListResponseStarted,
        SendFileListResponseCompleted,
        ReadFileListResponseStarted,
        ReadFileListResponseCompleted,
        SendFileListRequestStarted,
        SendFileListRequestCompleted,

        SendFileBytesStarted,
        SentFileChunkToClient,
        SendFileBytesCompleted,

        AppendUnreadBytesToInboundFileTransfer,
        ReceiveFileBytesStarted,
        ReceivedClientMessageDataFromSocket,
        ReceivedFileBytesFromSocket,
        LastSocketReadContainedUnreadBytes,
        CopiedUnreadBytesToIncomingMessageData,
        CopiedUnreadBytesToIncomingFileTransfer,
        FileTransferProgress,
        ReceiveFileBytesCompleted,

        SendConfirmationMessageStarted,
        SendConfirmationMessageCompleted,
        ReceiveConfirmationMessageStarted,
        ReceiveConfirmationMessageCompleted,

        NotifyClientDataIsNoLongerBeingReceivedStarted,
        NotifyClientDataIsNoLongerBeingReceivedCompleted,
        AbortOutboundFileTransfer,

        ShutdownListenSocketStarted,
        ShutdownListenSocketCompleted,
        ShutdownTransferSocketStarted,
        ShutdownTransferSocketCompleted,

        ErrorOccurred
    }
}
