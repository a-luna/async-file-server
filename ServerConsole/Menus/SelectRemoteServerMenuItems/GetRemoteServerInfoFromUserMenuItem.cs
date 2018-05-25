namespace ServerConsole.Menus.SelectRemoteServerMenuItems
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
            
            var requestServerInfoResult = await RequestAdditionalInfoFromRemoteServerAsync();

            return requestServerInfoResult.Success
                ? Result.Ok()
                : requestServerInfoResult;
        }

        async Task<Result> RequestAdditionalInfoFromRemoteServerAsync()
        {
            var clientIp = _state.SelectedServer.SessionIpAddress;
            var clientPort = _state.SelectedServer.Port;

            if (_state.SelectedServer.IsEqualTo(_state.LocalServer.Info))
            {
                var error = $"{clientIp}:{clientPort} is the same IP address and port number used by this server.";
                return Result.Fail(error);
            }

            if (SharedFunctions.ClientAlreadyAdded(_state.SelectedServer, _state.Settings.RemoteServers))
            {
                _state.SelectedServer =
                    SharedFunctions.GetRemoteServer(_state.SelectedServer, _state.Settings.RemoteServers);

                return Result.Ok();
            }
            
            var requestServerInfoResult = await RequestServerInfoAsync();
            if (requestServerInfoResult.Failure)
            {
                return requestServerInfoResult;
            }

            _state.Settings.RemoteServers.Add(_state.SelectedServer);
            ServerSettings.SaveToFile(_state.Settings, _state.SettingsFilePath);

            return Result.Ok();
        }

        async Task<Result> RequestServerInfoAsync()
        {
            var remoteIp = _state.SelectedServer.SessionIpAddress;
            var remotePort = _state.SelectedServer.Port;

            _state.WaitingForServerInfoResponse = true;

            var requestServerInfoResult =
                await _state.LocalServer.RequestServerInfoAsync(
                        remoteIp,
                        remotePort)
                    .ConfigureAwait(false);

            if (requestServerInfoResult.Failure)
            {
                var error = $"Error requesting public IP address from new client:" +
                            Environment.NewLine + requestServerInfoResult.Error;

                return Result.Fail(error);
            }

            while (_state.WaitingForServerInfoResponse) { }

            return Result.Ok();
        }
    }
}
