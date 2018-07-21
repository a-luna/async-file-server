namespace AaronLuna.AsyncFileServer.Console.Menus.ServerConfigurationMenus.LocalServerNetworkPropertiesMenuItems
{
    using System;
    using System.Threading.Tasks;

    using Common.Console.Menu;
    using Common.Network;
    using Common.Result;

    class DisplayTcpIPv4StatsMenuItem : IMenuItem
    {
        readonly AppState _state;

        public DisplayTcpIPv4StatsMenuItem(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = "Display TCP IPv4 Statistics";
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public Task<Result> ExecuteAsync()
        {
            return Task.Run(() => Execute());
        }

        Result Execute()
        {
            Console.Clear();
            SharedFunctions.DisplayLocalServerInfo(_state);
            Console.WriteLine($"################# TCP IPV4 STATS ################{Environment.NewLine}");
            NetworkUtilities.DisplayIPv4TcpStatistics();

            Console.WriteLine($"{Environment.NewLine}Press enter to return to the main menu.");
            Console.ReadLine();

            return Result.Ok();
        }
    }
}
