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
        ReceiveTextMessageStarted,
        ReceiveTextMessageCompleted,

        SendInboundFileTransferInfoStarted,
        SendInboundFileTransferInfoCompleted,
        ReceiveInboundFileTransferInfoStarted,
        ReceiveInboundFileTransferInfoCompleted,

        SendOutboundFileTransferInfoStarted,
        SendOutboundFileTransferInfoCompleted,
        ReceiveOutboundFileTransferInfoStarted,
        ReceiveOutboundFileTransferInfoCompleted,

        SendTransferFolderRequestStarted,
        SendTransferFolderRequestCompleted,
        ReceiveTransferFolderRequestStarted,
        ReceiveTransferFolderRequestCompleted,
        SendTransferFolderResponseStarted,
        SendTransferFolderResponseCompleted,
        ReceiveTransferFolderResponseStarted,
        ReceiveTransferFolderResponseCompleted,

        SendPublicIpRequestStarted,
        SendPublicIpRequestCompleted,
        ReceivePublicIpRequestStarted,
        ReceivePublicIpRequestCompleted,
        SendPublicIpResponseStarted,
        SendPublicIpResponseCompleted,
        ReceivePublicIpResponseStarted,
        ReceivePublicIpResponseCompleted,

        ReceiveFileListRequestStarted,
        ReceiveFileListRequestCompleted,
        SendFileListResponseStarted,
        SendFileListResponseCompleted,
        ReceiveFileListResponseStarted,
        ReceiveFileListResponseCompleted,
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
