namespace AaronLuna.AsyncFileServer.Model
{
    using System;

    public enum TextMessageAuthor
    {
        None,
        Self,
        RemoteServer
    }

    public class TextMessage
    {
        public int SessionId { get; set; }
        public DateTime TimeStamp { get; set; }
        public TextMessageAuthor Author { get; set; }
        public string Message { get; set; }
        public bool Unread { get; set; }

        public override string ToString()
        {
            var author = Author == TextMessageAuthor.RemoteServer
                ? "Remote Server"
                : "You";

            return
                $"{author} wrote ({TimeStamp:MM/dd/yyyy hh:mm tt}):{Environment.NewLine}{Message}";
        }
    }
}
