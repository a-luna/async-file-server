namespace AaronLuna.AsyncFileServer.Console.Menus.RemoteServerMenus.SelectRemoteServerMenuItems
{
    using System;
    using System.Threading.Tasks;

    using Common.Console.Menu;
    using Common.Result;

    using Model;

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
            var remoteServerIp =
                SharedFunctions.GetIpAddressFromUser(Resources.Prompt_SetRemoteServerIp);

            var remoteServerPort =
                SharedFunctions.GetPortNumberFromUser(
                    Resources.Prompt_SetRemoteServerPortNumber,
                    false);

            var serverInfo = new ServerInfo(remoteServerIp, remoteServerPort);

            var validateServerInfo = ValidateServerInfo(serverInfo);
            if (validateServerInfo.Failure)
            {
                return validateServerInfo;
            }

            var requestInfoFromServer = await RequestServerInfoAsync().ConfigureAwait(false);
            if (requestInfoFromServer.Failure)
            {
                return requestInfoFromServer;
            }

            _state.SelectedServerInfo.Name
                = SharedFunctions.SetSelectedServerName(_state.SelectedServerInfo);

            _state.Settings.RemoteServers.Add(_state.SelectedServerInfo);
            var saveSettings = ServerSettings.SaveToFile(_state.Settings, _state.SettingsFilePath);

            return saveSettings.Success
                ? Result.Ok()
                : saveSettings;
        }

         Result ValidateServerInfo(ServerInfo serverInfo)
        {
            var clientIp = serverInfo.SessionIpAddress;
            var clientPort = serverInfo.PortNumber;

            if (serverInfo.IsEqualTo(_state.LocalServer.Info))
            {
                var error =
                    $"{clientIp}:{clientPort} is the same IP address " +
                    "and port number used by this server.";

                return Result.Fail(error);
            }

            var serverInfoAlreadyExists =
                SharedFunctions.ServerInfoAlreadyExists(serverInfo, _state.Settings.RemoteServers);

            if (serverInfoAlreadyExists)
            {
                _state.SelectedServerInfo =
                    SharedFunctions.GetRemoteServer(serverInfo, _state.Settings.RemoteServers).Value;

                return Result.Fail("Server is already added, returning to main menu.");
            }

            _state.SelectedServerInfo = serverInfo;

            return Result.Ok();
        }

        async Task<Result> RequestServerInfoAsync()
        {
            _state.WaitingForServerInfoResponse = true;

            var requestServerInfoResult =
                await _state.LocalServer.RequestServerInfoAsync(
                        _state.SelectedServerInfo.SessionIpAddress,
                        _state.SelectedServerInfo.PortNumber)
                    .ConfigureAwait(false);

            if (requestServerInfoResult.Failure)
            {
                var error =
                    "Error requesting additional info from remote server:" +
                    Environment.NewLine + requestServerInfoResult.Error;

                return Result.Fail(error);
            }

            while (_state.WaitingForServerInfoResponse) { }

            return Result.Ok();
        }
    }
}
