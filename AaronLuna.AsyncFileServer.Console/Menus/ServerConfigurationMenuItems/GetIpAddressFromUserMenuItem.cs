namespace AaronLuna.AsyncFileServer.Console.Menus.ServerConfigurationMenuItems
{
    using System;
    using System.Threading.Tasks;
    using Common.Console.Menu;
    using Common.Result;

    class GetIpAddressFromUserMenuItem : IMenuItem
    {
        readonly AppState _state;

        public GetIpAddressFromUserMenuItem(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = $"Change IP address ({_state.SelectedServerInfo.SessionIpAddress})";
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public Task<Result> ExecuteAsync()
        {
            return Task.Run((Func<Result>)Execute);
        }

        Result Execute()
        {
            _state.SelectedServerInfo.SessionIpAddress = 
                SharedFunctions.GetIpAddressFromUser(Resources.Prompt_ChangeRemoteServerIp);

            return Result.Ok();
        }

    }
}
