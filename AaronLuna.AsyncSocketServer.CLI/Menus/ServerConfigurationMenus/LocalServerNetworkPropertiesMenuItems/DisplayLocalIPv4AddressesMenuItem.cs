using System;
using System.Threading.Tasks;
using AaronLuna.Common.Console.Menu;
using AaronLuna.Common.Network;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncSocketServer.CLI.Menus.ServerConfigurationMenus.LocalServerNetworkPropertiesMenuItems
{
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

            Console.WriteLine(Environment.NewLine + Resources.Prompt_ReturnToPreviousMenu);
            Console.ReadLine();

            return Result.Ok();
        }
    }
}
