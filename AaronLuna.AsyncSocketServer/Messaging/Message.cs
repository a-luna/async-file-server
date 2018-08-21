using System;

namespace AaronLuna.AsyncSocketServer.Messaging
{
    public enum TextMessageAuthor
    {
        None,
        Self,
        RemoteServer
    }

    public class Message
    {
        public int SessionId { get; set; }
        public ServerInfo RemoteServerInfo { get; set; }
        public DateTime TimeStamp { get; set; }
        public TextMessageAuthor Author { get; set; }
        public string Text { get; set; }
        public bool Unread { get; set; }

        public override string ToString()
        {
            var author = Author == TextMessageAuthor.RemoteServer
                ? "Remote Server"
                : "You";

            return
                $"{author} wrote ({TimeStamp:MM/dd/yyyy hh:mm tt}):{Environment.NewLine}{Text}";
        }

        public Message Duplicate()
        {
            var shallowCopy = (Message) MemberwiseClone();

            shallowCopy.RemoteServerInfo = RemoteServerInfo.Duplicate();
            shallowCopy.TimeStamp = new DateTime(TimeStamp.Ticks);
            shallowCopy.Author = Author;
            shallowCopy.Text = string.Copy(Text);

            return shallowCopy;
        }
    }
}
