namespace AaronLuna.AsyncFileServer.Console.Menus.ServerConfigurationMenuItems
{
    using System;
    using System.Threading.Tasks;
    using Common.Console.Menu;
    using Common.Network;
    using Common.Result;

    class DisplayLocalIPv4AddressesMenuItem : IMenuItem
    {
        public DisplayLocalIPv4AddressesMenuItem()
        {
            ReturnToParent = false;
            ItemText = "Display Local IPv4 Address Info" + Environment.NewLine;

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
