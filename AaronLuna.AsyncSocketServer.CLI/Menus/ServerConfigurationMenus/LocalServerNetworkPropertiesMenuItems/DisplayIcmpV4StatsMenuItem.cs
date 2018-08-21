using System;
using System.Threading.Tasks;
using AaronLuna.Common.Console.Menu;
using AaronLuna.Common.Network;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncSocketServer.CLI.Menus.ServerConfigurationMenus.LocalServerNetworkPropertiesMenuItems
{
    class DisplayIcmpV4StatsMenuItem : IMenuItem
    {
        readonly AppState _state;

        public DisplayIcmpV4StatsMenuItem(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = $"Display ICMPv4 Statistics{Environment.NewLine}";
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
            Console.WriteLine($"################## ICMPV4 STATS #################{Environment.NewLine}");
            NetworkUtilities.DisplayIcmpV4Statistics();

            Console.WriteLine(Environment.NewLine + Resources.Prompt_ReturnToPreviousMenu);
            Console.ReadLine();

            return Result.Ok();
        }
    }
}
