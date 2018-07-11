namespace AaronLuna.AsyncFileServer.Console.Menus.EventLogsMenus.EventLogsMenuItems
{
    using System;
    using System.Threading.Tasks;

    using Common.Console.Menu;
    using Common.Result;

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
            var eventLog = _state.LocalServer.GetCompleteEventLog(_state.Settings.LogLevel);
            if (eventLog.Count == 0)
            {
                var error =
                    $"{Environment.NewLine}There are currently no events in the log, " +
                    "press enter to return to the main menu";

                Console.WriteLine(error);
                Console.ReadLine();
                return Result.Ok();
            }

            SharedFunctions.DisplayLocalServerInfo(_state);
            Console.WriteLine($"################### EVENT LOG ###################{Environment.NewLine}");
            foreach (var serverEvent in eventLog)
            {
                Console.WriteLine(serverEvent.ToString());
            }

            Console.WriteLine($"{Environment.NewLine}Press enter to return to the previous menu.");
            Console.ReadLine();

            return Result.Ok();
        }
    }
}
