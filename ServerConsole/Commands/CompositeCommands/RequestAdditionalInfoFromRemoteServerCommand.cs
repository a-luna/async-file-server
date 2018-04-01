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
        RemoteServer _newClient;
        readonly Logger _log = new Logger(typeof(RequestAdditionalInfoFromRemoteServerCommand));

        public RequestAdditionalInfoFromRemoteServerCommand(AppState state, RemoteServer newClient)
        {
            _log.Info("Begin: Instantiate RequestAdditionalInfoFromRemoteServerCommand");

            ReturnToParent = false;
            ItemText = "Request transfer folder and public IP dddress from remote server";
            _state = state;
            _newClient = newClient;

            _log.Info("Complete: Instantiate RequestAdditionalInfoFromRemoteServerCommand");
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public async Task<Result> ExecuteAsync()
        {
            _log.Info("Begin: RequestAdditionalInfoFromRemoteServerCommand.ExecuteAsync");

            var publicIp = _newClient.ConnectionInfo.PublicIpAddress;
            var clientIp = _newClient.ConnectionInfo.SessionIpAddress;
            var clientPort = _newClient.ConnectionInfo.Port;

            if (_newClient.ConnectionInfo.IsEqualTo(_state.MyInfo))
            {
                _log.Error($"Error: User tried to add this server's endpoint as a new client (RequestAdditionalInfoFromRemoteServerCommand.ExecuteAsync)");
                var error = $"{clientIp}:{clientPort} is the same IP address and port number used by this server.";
                return Result.Fail(error);
            }

            if (ConsoleStatic.ClientAlreadyAdded(_newClient, _state.Settings.RemoteServers))
            {
                _newClient = ConsoleStatic.GetRemoteServer(_newClient, _state.Settings.RemoteServers);

                _state.ClientInfo = _newClient.ConnectionInfo;
                _state.ClientTransferFolderPath = _newClient.TransferFolder;

                return Result.Ok();
            }

            var requestFolderPathCommand =
                new RequestTransferFolderCommand(_state, clientIp, clientPort);

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
                    new RequestPublicIpAddressCommand(_state, clientIp, clientPort);

                var requestPublicIpResult = await requestPublicIpCommand.ExecuteAsync();
                if (requestPublicIpResult.Failure)
                {
                    _log.Error($"Error: {requestPublicIpResult.Error} (RequestAdditionalInfoFromRemoteServerCommand.ExecuteAsync)");
                    return Result.Fail<RemoteServer>(requestPublicIpResult.Error);
                }                
            }

            Console.WriteLine($"{Environment.NewLine}Thank you! Connection info for new client has been successfully configured.\n");

            _state.Settings.RemoteServers.Add(_newClient);
            AppSettings.SaveToFile(_state.Settings, _state.SettingsFilePath);

            _log.Info("Complete: RequestAdditionalInfoFromRemoteServerCommand.ExecuteAsync");
            return Result.Ok();
        }
    }
}
