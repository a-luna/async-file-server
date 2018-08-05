using System;

namespace AaronLuna.AsyncFileServer.Model
{
    public class Request
    {
        public Request()
        {
            TimeStamp = DateTime.Now;
        }

        public DateTime TimeStamp { get; }
        public byte[] RequestBytes { get; set; }
        public RequestDirection Direction { get; set; }
    }
}
