using System;
using System.Threading.Tasks;
using AaronLuna.Common.Console.Menu;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncSocketServer.CLI.Menus.EventLogsMenus.EventLogsMenuItems
{
    class ViewAllEventsMenuItem : IMenuItem
    {
        readonly AppState _state;

        public ViewAllEventsMenuItem(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = "All events";
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

            var eventLog = _state.LocalServer.GetCompleteEventLog(_state.Settings.LogLevel);
            if (eventLog.Count == 0)
            {
                return Result.Fail("There are currently no events in the log");
            }

            Console.WriteLine($"################### EVENT LOG ###################{Environment.NewLine}");
            foreach (var serverEvent in eventLog)
            {
                Console.WriteLine(serverEvent.ToString());
            }

            Console.WriteLine(Environment.NewLine + Resources.Prompt_ReturnToPreviousMenu);
            Console.ReadLine();

            return Result.Ok();
        }
    }
}
