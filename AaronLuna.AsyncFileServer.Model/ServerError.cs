namespace AaronLuna.AsyncFileServer.Model
{
    using System;

    public class ServerError
    {
        public ServerError(string message)
        {
            Message = message;
            TimeStamp = DateTime.Now;
            Unread = true;
        }

        public DateTime TimeStamp { get; set; }
        public string Message { get; set; }
        public bool Unread { get; set; }

        public override string ToString()
        {
            return $"[{TimeStamp:MM/dd/yyyy HH:mm:ss.fff}]: {Message}";
        }
    }
}
