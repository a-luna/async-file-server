namespace TplSocketServer
{
    public enum MessageType
    {
        None                                  = 0,
        TextMessage                           = 1,
        InboundFileTransfer                   = 2,
        OutboundFileTransfer                  = 3,
        FileListRequest                       = 4,
        FileListResponse                      = 5,
        TransferFolderPathRequest             = 6,
        TransferFolderPathResponse            = 7,
        PublicIpAddressRequest                = 8,
        PublicIpAddressResponse               = 9,
        NoFilesAvailableForDownload           = 10,
        FileTransferAccepted                  = 11,
        FileTransferRejected                  = 12,
        FileTransferStalled                   = 13,
        //FileTransferCanceled                  = 14,
        RetryOutboundFileTransfer             = 15,
        RequestedFolderDoesNotExist           = 16,
        ShutdownServer                        = 17
    }
}
