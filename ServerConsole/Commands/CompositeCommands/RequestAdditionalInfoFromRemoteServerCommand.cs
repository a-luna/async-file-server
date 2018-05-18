namespace ServerConsole.Commands.CompositeCommands
{
    using System;
    using System.Net;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Logging;
    using AaronLuna.Common.Result;

    using ServerCommands;

    using TplSockets;

    class RequestAdditionalInfoFromRemoteServerCommand : ICommand
    {
        readonly AppState _state;
        readonly Logger _log = new Logger(typeof(RequestAdditionalInfoFromRemoteServerCommand));

        public RequestAdditionalInfoFromRemoteServerCommand(AppState state)
        {   ReturnToParent = false;

            ItemText = "Request transfer folder and public IP dddress from remote server";
            _state = state;
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public async Task<Result> ExecuteAsync()
        {
            var publicIp = _state.RemoteServerInfo.PublicIpAddress;
            var clientIp = _state.RemoteServerInfo.SessionIpAddress;
            var clientPort = _state.RemoteServerInfo.Port;

            if (_state.RemoteServerInfo.IsEqualTo(_state.LocalServer.Info))
            {
                _log.Error("Error: User tried to add this server\'s endpoint as a new client (RequestAdditionalInfoFromRemoteServerCommand.ExecuteAsync)");
                var error = $"{clientIp}:{clientPort} is the same IP address and port number used by this server.";
                return Result.Fail(error);
            }

            if (SharedFunctions.ClientAlreadyAdded(_state.RemoteServerInfo, _state.Settings.RemoteServers))
            {
                return Result.Ok();
            }

            var requestFolderPathCommand =
                new RequestTransferFolderCommand(_state);

            var requestFolderPathResult = await requestFolderPathCommand.ExecuteAsync();
            if (requestFolderPathResult.Failure)
            {
                _log.Error(
                    $"Error: {requestFolderPathResult.Error} (RequestAdditionalInfoFromRemoteServerCommand.ExecuteAsync)");
                return Result.Fail(requestFolderPathResult.Error);
            }

            if (!Equals(clientIp, publicIp) || Equals(publicIp, IPAddress.None))
            {
                var requestPublicIpCommand =
                    new RequestPublicIpAddressCommand(_state);

                var requestPublicIpResult = await requestPublicIpCommand.ExecuteAsync();
                if (requestPublicIpResult.Failure)
                {
                    _log.Error($"Error: {requestPublicIpResult.Error} (RequestAdditionalInfoFromRemoteServerCommand.ExecuteAsync)");
                    return Result.Fail<ServerInfo>(requestPublicIpResult.Error);
                }                
            }

            Console.WriteLine($"{Environment.NewLine}Thank you! Connection info for new client has been successfully configured.\n");

            _state.Settings.RemoteServers.Add(_state.RemoteServerInfo);
            ServerSettings.SaveToFile(_state.Settings, _state.SettingsFilePath);
            return Result.Ok();
        }
    }
}
