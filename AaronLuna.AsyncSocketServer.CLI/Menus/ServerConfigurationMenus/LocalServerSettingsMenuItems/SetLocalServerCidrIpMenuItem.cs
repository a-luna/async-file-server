using System;
using System.Threading.Tasks;
using AaronLuna.Common.Console.Menu;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncSocketServer.CLI.Menus.ServerConfigurationMenus.LocalServerSettingsMenuItems
{
    class SetLocalServerCidrIpMenuItem : IMenuItem
    {
        readonly AppState _state;

        public SetLocalServerCidrIpMenuItem(AppState state)
        {
            _state = state;

            ReturnToParent = true;
            ItemText = "*LAN CIDR IP.............................: " +
                       _state.Settings.LocalNetworkCidrIp + Environment.NewLine;
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public Task<Result> ExecuteAsync()
        {
            return Task.Run((Func<Result>)Execute);
        }

        Result Execute()
        {
            _state.Settings.LocalNetworkCidrIp = SharedFunctions.InitializeLanCidrIp(_state);
            _state.RestartRequired = true;

            return Result.Ok();
        }
    }
}
