using System.Linq;
using AaronLuna.Common.Extensions;

namespace AaronLuna.AsyncFileServer.Console.Menus.PendingRequestsMenus.PendingRequestsMenuItems
{
    using System;
    using System.Threading.Tasks;

    using Common.Console.Menu;
    using Common.Result;

    using Model;

    class ReadTextMessageMenuItem : IMenuItem
    {
        readonly AppState _state;
        readonly TextSession _textSession;

        public ReadTextMessageMenuItem(AppState state, TextSession textSession)
        {
            _state = state;
            _textSession = textSession;

            ReturnToParent = false;

            var message = textSession.UnreadMessages.Count > 1
                ? "messages"
                : "message";

            ItemText = $"Read {textSession.UnreadMessages.Count} {message} from {textSession.RemoteServerInfo}";
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public async Task<Result> ExecuteAsync()
        {
            Console.Clear();
            SharedFunctions.DisplayLocalServerInfo(_state);
            Console.WriteLine(Environment.NewLine);

            var unreadMessages = _textSession.UnreadMessages;
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
                    _textSession.RemoteServerInfo)
                    .ConfigureAwait(false);
        }
    }
}
