namespace ServerConsole.Menus.ServerConfigurationMenuItems
{
    using System;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Network;
    using AaronLuna.Common.Result;

    class DisplayLocalIPv4AddressesMenuItemcs : IMenuItem
    {
        public DisplayLocalIPv4AddressesMenuItemcs()
        {
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
            NetworkUtilities.DisplayLocalIPv4AddressInfo();

            Console.WriteLine($"{Environment.NewLine}Press enter to return to the main menu.");
            Console.ReadLine();

            return Result.Ok();
        }
    }
}
