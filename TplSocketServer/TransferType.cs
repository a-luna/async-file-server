namespace TplSocketServer
{
    using System;

    [Flags]
    public enum TransferType
    {
        None                    = 0x00,
        TextMessage             = 0x01,
        InboundFileTransfer     = 0x02,
        OutboundFileTransfer    = 0x03,
        GetListOfFiles          = 0x04
    }
}
