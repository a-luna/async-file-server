namespace AaronLuna.AsyncFileServer.Console.Menus.ServerConfigurationMenuItems
{
    using System;
    using System.Threading.Tasks;
    using Common.Console.Menu;
    using Common.Result;

    class SetLocalServerCidrIpMenuItem : IMenuItem
    {
        readonly AppState _state;

        public SetLocalServerCidrIpMenuItem(AppState state)
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
