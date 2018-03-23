namespace ServerConsole.Commands.CompositeCommands
{
    using System;
    using System.Net;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Result;

    using ServerCommands;

    using TplSocketServer;

    class RequestAdditionalInfoFromRemoteServerCommand : ICommand
    {
        readonly AppState _state;
        RemoteServer _newClient;

        public RequestAdditionalInfoFromRemoteServerCommand(AppState state, RemoteServer newClient)
        {
            ReturnToParent = false;
            ItemText = "Request transfer folder and public IP dddress from remote server";
            _state = state;
            _newClient = newClient;
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public async Task<Result> ExecuteAsync()
        {
            var publicIp = _newClient.ConnectionInfo.PublicIpAddress;
            var clientIp = _newClient.ConnectionInfo.SessionIpAddress;
            var clientPort = _newClient.ConnectionInfo.Port;

            if (_newClient.ConnectionInfo.IsEqualTo(_state.MyInfo))
            {
                var error = $"{clientIp}:{clientPort} is the same IP address and port number used by this server.";
                return Result.Fail(error);
            }

            if (ConsoleStatic.ClientAlreadyAdded(_newClient, _state.Settings.RemoteServers))
            {
                _newClient = ConsoleStatic.GetRemoteServer(_newClient, _state.Settings.RemoteServers);
                return Result.Ok();
            }

            var requestFolderPathCommand =
                new RequestTransferFolderCommand(_state, clientIp, clientPort);

            var requestFolderPathResult = await requestFolderPathCommand.ExecuteAsync();
            if (requestFolderPathResult.Failure)
            {
                return Result.Fail(requestFolderPathResult.Error);
            }

            _newClient.TransferFolder = _state.ClientTransferFolderPath;

            if (!Equals(clientIp, publicIp) || Equals(publicIp, IPAddress.None))
            {
                var requestPublicIpCommand =
                    new RequestPublicIpAddressCommand(_state, clientIp, clientPort);

                var requestPublicIpResult = await requestPublicIpCommand.ExecuteAsync();
                if (requestPublicIpResult.Failure)
                {
                    return Result.Fail<RemoteServer>(requestPublicIpResult.Error);
                }

                _newClient.ConnectionInfo.PublicIpAddress = _state.ClientInfo.PublicIpAddress;
            }

            Console.WriteLine($"{Environment.NewLine}Thank you! Connection info for new client has been successfully configured.\n");

            _state.Settings.RemoteServers.Add(_newClient);
            AppSettings.SaveToFile(_state.Settings, _state.SettingsFilePath);

            return Result.Ok();
        }
    }
}
