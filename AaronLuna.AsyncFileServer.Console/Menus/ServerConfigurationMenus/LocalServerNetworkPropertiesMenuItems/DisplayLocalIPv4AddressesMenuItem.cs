namespace AaronLuna.AsyncFileServer.Console.Menus.ServerConfigurationMenus.LocalServerNetworkPropertiesMenuItems
{
    using System;
    using System.Threading.Tasks;

    using Common.Console.Menu;
    using Common.Network;
    using Common.Result;

    class DisplayLocalIPv4AddressesMenuItem : IMenuItem
    {
        readonly AppState _state;

        public DisplayLocalIPv4AddressesMenuItem(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = "Display Local IPv4 Address Info";
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
            Console.WriteLine($"############ LOCAL IPV4 ADDRESS INFO ############{Environment.NewLine}");
            NetworkUtilities.DisplayLocalIPv4AddressInfo();

            Console.WriteLine($"{Environment.NewLine}Press enter to return to the main menu.");
            Console.ReadLine();

            return Result.Ok();
        }
    }
}
