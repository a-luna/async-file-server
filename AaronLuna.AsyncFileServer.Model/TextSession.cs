namespace AaronLuna.AsyncFileServer.Model
{
    using System.Linq;

    using System.Collections.Generic;

    public class TextSession
    {
        public TextSession()
        {
            Messages = new List<TextMessage>();
        }

        public int Id { get; set; }
        public ServerInfo RemoteServerInfo { get; set; }
        public List<TextMessage> Messages { get; set; }
        public int MessageCount => Messages.Count;
        public List<TextMessage> UnreadMessages => Messages.Select(m => m).Where(m => m.Unread).ToList();

        public override string ToString()
        {
            return $"{RemoteServerInfo} ({MessageCount} total messages)";
        }
    }
}
