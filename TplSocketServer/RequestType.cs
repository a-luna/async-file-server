namespace TplSocketServer
{
    using System;

    [Flags]
    public enum RequestType
    {
        None                    = 0x00,
        TextMessage             = 0x01,
        InboundFileTransfer     = 0x02,
        OutboundFileTransfer    = 0x03,
        GetFileList             = 0x04,
        ReceiveFileList         = 0x05
    }
}
