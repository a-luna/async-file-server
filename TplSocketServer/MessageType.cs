namespace TplSocketServer
{
    public enum MessageType
    {
        None                                  = 0,
        ConnectionAccepted                    = 1,
        TextMessage                           = 2,
        InboundFileTransfer                   = 3,
        OutboundFileTransfer                  = 4,
        FileListRequest                       = 5,
        FileListResponse                      = 6,
        TransferFolderPathRequest             = 7,
        TransferFolderPathResponse            = 8,
        PublicIpAddressRequest                = 9,
        PublicIpAddressResponse               = 10,
        NoFilesAvailableForDownload           = 11,
        FileTransferAccepted                  = 12,
        FileTransferRejected                  = 13,
        FileTransferStalled                   = 14,
        //FileTransferCanceled                  = 14,
        RetryOutboundFileTransfer             = 15,
        RequestedFolderDoesNotExist           = 16,
        ShutdownServer                        = 17
    }
}
