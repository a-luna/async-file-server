using System;

namespace AaronLuna.AsyncFileServer.Model
{
    public class ServerRequest
    {
        public ServerRequest()
        {
            TimeStamp = DateTime.Now;
        }

        public DateTime TimeStamp { get; }
        public byte[] RequestBytes { get; set; }
        public ServerRequestDirection Direction { get; set; }
    }
}
