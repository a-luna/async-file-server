namespace ServerConsole.Commands.CompositeCommands
{
    using System;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Result;

    using TplSockets;

    class GetRemoteServerInfoFromUserMenuItem : IMenuItem
    {
        readonly AppState _state;

        public GetRemoteServerInfoFromUserMenuItem(AppState state)
        {
            ReturnToParent = false;
            ItemText = "Add new client";

            _state = state;
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public async Task<Result> ExecuteAsync()
        {
            var remoteServerIp = SharedFunctions.GetIpAddressFromUser($"{Environment.NewLine}Enter the client's IPv4 address:");
            var remoteServerPort = SharedFunctions.GetPortNumberFromUser($"{Environment.NewLine}Enter the client's port number:", false);
            _state.SelectedServer = new ServerInfo(remoteServerIp, remoteServerPort);

            var requestServerInfo = new RequestAdditionalInfoFromRemoteServerMenuItem(_state);
            var requestServerInfoResult = await requestServerInfo.ExecuteAsync();

            return requestServerInfoResult.Success
                ? Result.Ok()
                : requestServerInfoResult;
        }
    }
}
