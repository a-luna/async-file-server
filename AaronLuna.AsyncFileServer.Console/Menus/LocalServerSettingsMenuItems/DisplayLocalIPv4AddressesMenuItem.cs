using System;
using System.Threading.Tasks;
using AaronLuna.Common.Console.Menu;
using AaronLuna.Common.Network;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncFileServer.Console.Menus.LocalServerSettingsMenuItems
{
    class DisplayLocalIPv4AddressesMenuItem : IMenuItem
    {
        public DisplayLocalIPv4AddressesMenuItem()
        {
            ReturnToParent = false;
            ItemText = $"Display Local IPv4 Address Info{Environment.NewLine}";

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

            System.Console.WriteLine($"{Environment.NewLine}Press enter to return to the main menu.");
            System.Console.ReadLine();

            return Result.Ok();
        }
    }
}
