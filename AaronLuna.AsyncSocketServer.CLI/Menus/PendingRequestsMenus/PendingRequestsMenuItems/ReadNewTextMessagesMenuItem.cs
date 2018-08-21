using System;
using System.Linq;
using System.Threading.Tasks;
using AaronLuna.AsyncSocketServer.Messaging;
using AaronLuna.Common.Console.Menu;
using AaronLuna.Common.Extensions;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncSocketServer.CLI.Menus.PendingRequestsMenus.PendingRequestsMenuItems
{
    class ReadNewTextMessagesMenuItem : IMenuItem
    {
        readonly AppState _state;
        readonly Conversation _conversation;

        public ReadNewTextMessagesMenuItem(AppState state, Conversation conversation)
        {
            _state = state;
            _conversation = conversation;

            ReturnToParent = false;

            var messagePlural = conversation.UnreadMessages.Count > 1
                ? "messages"
                : "message";

            ItemText = $"Read {conversation.UnreadMessages.Count} {messagePlural} from {conversation.RemoteServerInfo}";
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public async Task<Result> ExecuteAsync()
        {
            Console.Clear();
            SharedFunctions.DisplayLocalServerInfo(_state);
            Console.WriteLine(Environment.NewLine);

            var unreadMessages = _conversation.UnreadMessages;
            var prompt = string.Empty;

            foreach (var i in Enumerable.Range(0, unreadMessages.Count))
            {
                var textMessage = unreadMessages[i];
                textMessage.Unread = false;

                prompt += textMessage + Environment.NewLine + Environment.NewLine;

                if (i.IsLastIteration(unreadMessages.Count))
                {
                    prompt += "Reply?";
                }
            }

            var replyToMessage = SharedFunctions.PromptUserYesOrNo(_state, prompt);
            if (!replyToMessage) return Result.Ok();

            return await
                SharedFunctions.SendTextMessageAsync(
                    _state,
                    _conversation.RemoteServerInfo)
                    .ConfigureAwait(false);
        }
    }
}
