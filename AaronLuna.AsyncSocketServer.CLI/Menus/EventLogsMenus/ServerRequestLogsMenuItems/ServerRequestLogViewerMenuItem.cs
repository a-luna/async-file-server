using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AaronLuna.Common.Console.Menu;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncSocketServer.CLI.Menus.EventLogsMenus.ServerRequestLogsMenuItems
{
    class ServerRequestLogViewerMenuItem : IMenuItem
    {
        readonly AppState _state;
        readonly List<ServerEvent> _eventLog;

        public ServerRequestLogViewerMenuItem(
            AppState state,
            List<ServerEvent> eventLog,
            string itemText)
        {
            _state = state;
            _eventLog = eventLog;

            ReturnToParent = false;
            ItemText = itemText;
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public Task<Result> ExecuteAsync()
        {
            return Task.Run((Func<Result>)Execute);
        }

        Result Execute()
        {
            SharedFunctions.DisplayLocalServerInfo(_state);
            Console.WriteLine($"{Environment.NewLine}############ SERVER REQUEST EVENT LOG ############{Environment.NewLine}");
            foreach (var serverEvent in _eventLog)
            {
                Console.WriteLine(serverEvent.ToString());
            }

            Console.WriteLine(Environment.NewLine + Resources.Prompt_ReturnToPreviousMenu);
            Console.ReadLine();

            return Result.Ok();
        }
    }
}
