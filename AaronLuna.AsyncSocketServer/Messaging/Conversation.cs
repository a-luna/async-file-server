using System.Collections.Generic;
using System.Linq;

namespace AaronLuna.AsyncSocketServer.Messaging
{
    public class Conversation
    {
        public Conversation()
        {
            Messages = new List<Message>();
        }

        public int Id { get; set; }
        public ServerInfo RemoteServerInfo { get; set; }
        public List<Message> Messages { get; private set; }
        public int MessageCount => Messages.Count;
        public List<Message> UnreadMessages => Messages.Select(m => m).Where(m => m.Unread).ToList();

        public override string ToString()
        {
            return $"{RemoteServerInfo} ({MessageCount} total messages)";
        }

        public Conversation Duplicate()
        {
            var shallowCopy = (Conversation) MemberwiseClone();

            shallowCopy.RemoteServerInfo = RemoteServerInfo.Duplicate();
            shallowCopy.Messages = Messages.Select(m => m.Duplicate()).ToList();

            return shallowCopy;
        }
    }
}
