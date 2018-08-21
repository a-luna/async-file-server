using System;
using System.Threading.Tasks;
using AaronLuna.AsyncSocketServer.Messaging;
using AaronLuna.Common.Console.Menu;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncSocketServer.CLI.Menus.EventLogsMenus.TextMessageLogsMenuItems
{
    class ViewTextMessageLogMenuItem : IMenuItem
    {
        readonly AppState _state;
        readonly Conversation _conversation;

        public ViewTextMessageLogMenuItem(AppState state, Conversation conversation, bool isLastMenuItem)
        {
            _state = state;
            _conversation = conversation;

            ReturnToParent = false;
            ItemText = isLastMenuItem
                ? $"{conversation}{Environment.NewLine}"
                : conversation.ToString();
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
            foreach (var textMessage in _conversation.Messages)
            {
                Console.WriteLine(textMessage + Environment.NewLine);
            }

            Console.WriteLine(Environment.NewLine + Resources.Prompt_ReturnToPreviousMenu);
            Console.ReadLine();

            return Result.Ok();
        }
    }
}
