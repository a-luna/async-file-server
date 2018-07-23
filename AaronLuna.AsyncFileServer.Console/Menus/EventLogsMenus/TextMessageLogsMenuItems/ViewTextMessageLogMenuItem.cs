namespace AaronLuna.AsyncFileServer.Console.Menus.EventLogsMenus.TextMessageLogsMenuItems
{
    using System;
    using System.Threading.Tasks;

    using Model;
    using Common.Console.Menu;
    using Common.Result;

    class ViewTextMessageLogMenuItem : IMenuItem
    {
        readonly AppState _state;
        readonly TextSession _textSession;

        public ViewTextMessageLogMenuItem(AppState state, TextSession textSession, bool isLastMenuItem)
        {
            _state = state;
            _textSession = textSession;

            ReturnToParent = false;
            ItemText = isLastMenuItem
                ? $"{textSession}{Environment.NewLine}"
                : textSession.ToString();
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public Task<Result> ExecuteAsync()
        {
            return Task.Run((Func<Result>) Execute);
        }

        Result Execute()
        {
            SharedFunctions.DisplayLocalServerInfo(_state);
            Console.WriteLine($"{Environment.NewLine}############# TEXT MESSAGE EVENT LOG #############{Environment.NewLine}");
            foreach (var textMessage in _textSession.Messages)
            {
                Console.WriteLine(textMessage + Environment.NewLine);
            }

            Console.WriteLine($"{Environment.NewLine}Press enter to return to the previous menu.");
            Console.ReadLine();

            return Result.Ok();
        }
    }
}
