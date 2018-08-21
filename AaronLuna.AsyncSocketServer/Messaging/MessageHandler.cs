using System;
using System.Collections.Generic;
using System.Linq;
using AaronLuna.AsyncSocketServer.Requests.RequestTypes;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncSocketServer.Messaging
{
    public class MessageHandler
    {
        int _conversationId;
        readonly List<Conversation> _conversations;

        public MessageHandler()
        {
            _conversations = new List<Conversation>();
        }

        public List<Conversation> Conversations =>
            _conversations.Select(c => c.Duplicate()).ToList();

        public event EventHandler<ServerEvent> EventOccurred;

        public override string ToString()
        {
            var totalMessages = 0;
            foreach (var textSession in _conversations)
            {
                totalMessages += textSession.MessageCount;
            }

            return
                $"[{totalMessages} Messages ({_conversations.Count} conversations)]";
        }

        public Result<Conversation> GetTextSessionById(int id)
        {
            var matches = _conversations.Select(ts => ts).Where(ts => ts.Id == id).ToList();

            if (matches.Count == 0)
            {
                return Result.Fail<Conversation>($"No text session was found with an ID value of {id}");
            }

            if (matches.Count > 1)
            {
                return Result.Fail<Conversation>($"Found {matches.Count} text sessions with the same ID value of {id}");
            }

            return Result.Ok(matches[0]);
        }

        public void AddNewSentMessage(MessageRequest messageRequest)
        {
            var textSessionId = GetTextSessionIdForRemoteServer(messageRequest.RemoteServerInfo);
            var textSession = GetTextSessionById(textSessionId).Value;

            var newMessage = new Message
            {
                SessionId = textSessionId,
                TimeStamp = DateTime.Now,
                RemoteServerInfo = messageRequest.RemoteServerInfo,
                Author = TextMessageAuthor.Self,
                Text = messageRequest.Message,
                Unread = false
            };

            textSession.Messages.Add(newMessage);
        }

        public void AddNewReceivedMessage(MessageRequest messageRequest)
        {
            var textSessionId = GetTextSessionIdForRemoteServer(messageRequest.RemoteServerInfo);
            var textSession = GetTextSessionById(textSessionId).Value;

            var newTextMessage = new Message
            {
                SessionId = textSessionId,
                TimeStamp = DateTime.Now,
                RemoteServerInfo = messageRequest.RemoteServerInfo,
                Author = TextMessageAuthor.RemoteServer,
                Text = messageRequest.Message,
                Unread = true
            };

            textSession.Messages.Add(newTextMessage);

            EventOccurred?.Invoke(this, new ServerEvent
            {
                EventType = EventType.ReceivedTextMessage,
                TextMessage = newTextMessage.Text,
                RemoteServerIpAddress = messageRequest.RemoteServerInfo.SessionIpAddress,
                RemoteServerPortNumber = messageRequest.RemoteServerInfo.PortNumber,
                TextSessionId = textSessionId
            });
        }

        int GetTextSessionIdForRemoteServer(ServerInfo remoteServerInfo)
        {
            Conversation match = null;
            foreach (var textSession in _conversations)
            {
                if (!textSession.RemoteServerInfo.IsEqualTo(remoteServerInfo)) continue;

                match = textSession;
                break;
            }

            if (match != null)
            {
                return match.Id;
            }

            var newTextSession = new Conversation
            {
                Id = _conversationId,
                RemoteServerInfo = remoteServerInfo
            };

            _conversations.Add(newTextSession);
            _conversationId++;

            return newTextSession.Id;
        }
    }
}
