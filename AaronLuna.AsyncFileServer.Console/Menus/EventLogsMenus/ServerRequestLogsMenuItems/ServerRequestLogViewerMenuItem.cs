namespace AaronLuna.AsyncFileServer.Console.Menus.EventLogsMenus.ServerRequestLogsMenuItems
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    using Common.Console.Menu;
    using Common.Result;

    using Controller;
    using Model;

    class ServerRequestLogViewerMenuItem : IMenuItem
    {
        readonly AppState _state;
        readonly ServerRequestController _request;
        readonly List<ServerEvent> _eventLog;
        readonly LogLevel _logLevel;

        public ServerRequestLogViewerMenuItem(
            AppState state,
            ServerRequestController request,
            List<ServerEvent> eventLog,
            LogLevel logLevel)
        {
            _state = state;
            _request = request;
            _eventLog = eventLog;
            _logLevel = logLevel;

            ReturnToParent = false;
            ItemText = request.ToString();
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
