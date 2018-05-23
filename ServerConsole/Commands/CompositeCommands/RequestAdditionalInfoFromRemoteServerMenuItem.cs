namespace ServerConsole.Commands.CompositeCommands
{
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Result;

    using ServerCommands;

    using TplSockets;

    class RequestAdditionalInfoFromRemoteServerMenuItem : IMenuItem
    {
        readonly AppState _state;

        public RequestAdditionalInfoFromRemoteServerMenuItem(AppState state)
        {   ReturnToParent = false;

            ItemText = "Request transfer folder and public IP dddress from remote server";
            _state = state;
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public async Task<Result> ExecuteAsync()
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

            var requestFolderPathCommand =
                new RequestTransferFolderMenuItem(_state);

            var requestFolderPathResult = await requestFolderPathCommand.ExecuteAsync();
            if (requestFolderPathResult.Failure)
            {
                return requestFolderPathResult;
            }

            var requestPublicIpCommand =
                new RequestPublicIpAddressMenuItem(_state);

            var requestPublicIpResult = await requestPublicIpCommand.ExecuteAsync();
            if (requestPublicIpResult.Failure)
            {
                return requestPublicIpResult;
            }

            _state.Settings.RemoteServers.Add(_state.SelectedServer);
            ServerSettings.SaveToFile(_state.Settings, _state.SettingsFilePath);

            return Result.Ok();
        }
    }
}
