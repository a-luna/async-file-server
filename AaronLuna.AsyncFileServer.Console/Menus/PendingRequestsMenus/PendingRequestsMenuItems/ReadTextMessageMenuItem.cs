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
            ItemText = $"Read new message from {textSession.RemoteServerInfo}";
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public async Task<Result> ExecuteAsync()
        {
            System.Console.WriteLine(Environment.NewLine);

            foreach (var textMessage in _textSession.UnreadMessages)
            {
                System.Console.WriteLine(textMessage + Environment.NewLine);
                textMessage.Unread = false;
            }

            var replyToMessage = SharedFunctions.PromptUserYesOrNo("Reply?");
            if (!replyToMessage) return Result.Ok();

            return await SharedFunctions.SendTextMessageAsync(_state).ConfigureAwait(false);
        }
    }
}
