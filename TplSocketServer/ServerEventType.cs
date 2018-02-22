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

        DetermineTransferTypeStarted,
        DetermineTransferTypeCompleted,

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
        SendFileBytesCompleted,

        ReceiveFileBytesStarted,
        ReceivedDataFromSocket,
        FileTransferProgress,
        ReceiveFileBytesCompleted,
        
        SendConfirmationMessageStarted,
        SendConfirmationMessageCompleted,
        ReceiveConfirmationMessageStarted,
        ReceiveConfirmationMessageCompleted,

        ShutdownListenSocketStarted,
        ShutdownListenSocketCompleted,
        ShutdownTransferSocketStarted,
        ShutdownTransferSocketCompleted,

        ErrorOccurred
    }
}
