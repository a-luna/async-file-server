namespace TplSocketServer
{
    public enum RequestType
    {
        None                            = 0,
        TextMessage                     = 1,
        InboundFileTransfer             = 2,
        OutboundFileTransfer            = 3,
        GetFileList                     = 4,
        ReceiveFileList                 = 5,
        TransferFolderPathRequest       = 6,
        TransferFolderPathResponse      = 7,
        PublicIpAddressRequest          = 8,
        PublicIpAddressResponse         = 9,
        DataIsNoLongerBeingReceived     = 10
    }
}
