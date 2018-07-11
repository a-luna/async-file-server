namespace AaronLuna.AsyncFileServer.Console.Menus.RemoteServerMenus.SelectRemoteServerMenuItems
{
    using System;
    using System.Threading.Tasks;

    using Common;
    using Common.Console.Menu;
    using Common.Result;

    using Model;

    class GetRemoteServerInfoFromUserMenuItem : IMenuItem
    {
        readonly AppState _state;

        public GetRemoteServerInfoFromUserMenuItem(AppState state)
        {
            ReturnToParent = false;
            ItemText = $"Add new client{Environment.NewLine}";

            _state = state;
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public async Task<Result> ExecuteAsync()
        {
            _state.DoNotRequestServerInfo = true;

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

            _state.DoNotRequestServerInfo = false;

            return saveSettings;
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
            var ipAddress = _state.SelectedServerInfo.SessionIpAddress;
            var port = _state.SelectedServerInfo.PortNumber;

            var requestServerInfoTask =
                Task.Run(() => 
                    SharedFunctions.RequestServerInfoAsync(_state, ipAddress, port));

            var requestServerInfoResult = Result.Fail("Request for server info timed out before receiving a response");

            if (requestServerInfoTask
                == await Task.WhenAny(
                    requestServerInfoTask,
                    Task.Delay(Constants.FiveSecondsInMilliseconds))
                    .ConfigureAwait(false))
            {
                requestServerInfoResult = await requestServerInfoTask;
            }
            
            return requestServerInfoResult;
        }
    }
}
