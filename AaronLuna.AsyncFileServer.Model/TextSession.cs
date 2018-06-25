namespace AaronLuna.AsyncFileServer.Model
{
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
    }
}
