namespace AaronLuna.AsyncFileServer.Console.Menus.ServerConfigurationMenus.LocalServerNetworkPropertiesMenuItems
{
    using System;
    using System.Threading.Tasks;

    using Common.Console.Menu;
    using Common.Network;
    using Common.Result;

    class DisplayIPv4GlobalStatsMenuItem : IMenuItem
    {
        readonly AppState _state;

        public DisplayIPv4GlobalStatsMenuItem(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = "Display IPv4 Global Statistics";
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
            Console.WriteLine($"############### IPV4 GLOBAL STATS ###############{Environment.NewLine}");
            NetworkUtilities.DisplayIPv4GlobalStatistics();

            Console.WriteLine($"{Environment.NewLine}Press enter to return to the main menu.");
            Console.ReadLine();

            return Result.Ok();
        }
    }
}
