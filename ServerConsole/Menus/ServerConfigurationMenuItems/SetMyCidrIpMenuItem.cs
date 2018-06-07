using AaronLuna.Common.Network;

namespace ServerConsole.Menus.ServerConfigurationMenuItems
{
    using System;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Result;

    class SetMyCidrIpMenuItem : IMenuItem
    {
        readonly AppState _state;

        public SetMyCidrIpMenuItem(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = $"Change CIDR IP for this LAN * ({_state.Settings.LocalNetworkCidrIp})";
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public Task<Result> ExecuteAsync()
        {
            return Task.Run((Func<Result>)Execute);
        }

        Result Execute()
        {
            _state.Settings.LocalNetworkCidrIp = SharedFunctions.InitializeLanCidrIp();
            _state.RestartRequired = true;

            return Result.Ok();
        }
    }
}
