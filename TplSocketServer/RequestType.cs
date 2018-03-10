namespace TplSocketServer
{
    public enum RequestType
    {
        None                            = 0x00,
        TextMessage                     = 0x01,
        InboundFileTransfer             = 0x02,
        OutboundFileTransfer            = 0x03,
        GetFileList                     = 0x04,
        ReceiveFileList                 = 0x05,
        TransferFolderPathRequest       = 0x06,
        TransferFolderPathResponse      = 0x07,
        PublicIpAddressRequest          = 0x08,
        PublicIpAddressResponse         = 0x09,
        DataIsNoLongerBeingReceived     = 0x10
    }
}
