namespace AaronLuna.AsyncFileServer.Console.Menus.EventLogsMenus.ServerRequestLogsMenuItems
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    using Common.Console.Menu;
    using Common.Result;

    using Model;

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

            Console.WriteLine($"{Environment.NewLine}Press enter to return to the previous menu.");
            Console.ReadLine();

            return Result.Ok();
        }
    }
}
