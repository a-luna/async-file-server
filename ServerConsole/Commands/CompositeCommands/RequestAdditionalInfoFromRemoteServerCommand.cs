using System;
using System.Net;
using System.Threading.Tasks;
using AaronLuna.Common.Console.Menu;
using AaronLuna.Common.Result;
using ServerConsole.Commands.ServerCommands;
using TplSocketServer;

namespace ServerConsole.Commands.CompositeCommands
{
    class RequestAdditionalInfoFromRemoteServerCommand : ICommand<RemoteServer>
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

        public async Task<CommandResult<RemoteServer>> ExecuteAsync()
        {
            var publicIp = _newClient.ConnectionInfo.PublicIpAddress;
            var clientIp = _newClient.ConnectionInfo.SessionIpAddress;
            var clientPort = _newClient.ConnectionInfo.Port;

            if (_newClient.ConnectionInfo.IsEqualTo(_state.MyInfo))
            {
                var result = Result.Fail<RemoteServer>(
                    $"{clientIp}:{clientPort} is the same IP address and port number used by this server.");

                return new CommandResult<RemoteServer>
                {
                    ReturnToParent = ReturnToParent,
                    Result = result
                };
            }

            if (ConsoleStatic.ClientAlreadyAdded(_newClient, _state.Settings.RemoteServers))
            {
                var result = Result.Fail<RemoteServer>(
                    "A client with the same IP address and Port # has already been added.");

                return new CommandResult<RemoteServer>
                {
                    ReturnToParent = ReturnToParent,
                    Result = result
                };
            }

            var requestFolderPathCommand =
                new RequestTransferFolderPathFromRemoteServerCommand(_state, clientIp, clientPort);

            var requestFolderPathCommandResult = await requestFolderPathCommand.ExecuteAsync();
            var requestFolderPathResult = requestFolderPathCommandResult.Result;

            if (requestFolderPathResult.Failure)
            {
                var result = Result.Fail<RemoteServer>(requestFolderPathResult.Error);

                return new CommandResult<RemoteServer>
                {
                    ReturnToParent = ReturnToParent,
                    Result = result
                };
            }

            _newClient.TransferFolder = requestFolderPathResult.Value;

            if (!Equals(clientIp, publicIp) || publicIp == IPAddress.None)
            {
                var requestPublicIpCommand =
                    new RequestPublicIpAddressFromRemoteServerCommand(_state, clientIp, clientPort);

                var requestPublicICommandResult = await requestPublicIpCommand.ExecuteAsync();
                var requestPublicIpResult = requestPublicICommandResult.Result;

                if (requestPublicIpResult.Failure)
                {
                    var result = Result.Fail<RemoteServer>(requestPublicIpResult.Error);

                    return new CommandResult<RemoteServer>
                    {
                        ReturnToParent = ReturnToParent,
                        Result = result
                    };
                }

                _newClient.ConnectionInfo.PublicIpAddress = requestPublicIpResult.Value;
            }

            Console.WriteLine($"{Environment.NewLine}Thank you! Connection info for new client has been successfully configured.\n");

            _state.Settings.RemoteServers.Add(_newClient);
            AppSettings.SaveToFile(_state.Settings, _state.SettingsFilePath);

            return new CommandResult<RemoteServer>
            {
                ReturnToParent = ReturnToParent,
                Result = Result.Ok(_newClient)
            };
        }
    }
}
