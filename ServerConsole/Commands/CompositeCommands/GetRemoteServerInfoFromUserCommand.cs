namespace ServerConsole.Commands.CompositeCommands
{
    using System;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Logging;
    using AaronLuna.Common.Result;

    using TplSocketServer;

    class GetRemoteServerInfoFromUserCommand : ICommand
    {
        AppState _state;
        RemoteServer _newClient;
        readonly Logger _log = new Logger(typeof(GetRemoteServerInfoFromUserCommand));

        public GetRemoteServerInfoFromUserCommand(AppState state)
        {
            _log.Info("Begin: Instantiate GetRemoteServerInfoFromUserCommand");

            ReturnToParent = false;
            ItemText = "Add new client";

            _state = state;

            _log.Info("Complete: Instantiate GetRemoteServerInfoFromUserCommand");
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public async Task<Result> ExecuteAsync()
        {
            _log.Info("Begin: GetRemoteServerInfoFromUserCommand.ExecuteAsync");
            //_state.IgnoreIncomingConnections = true;

            _newClient = new RemoteServer();
            var clientInfoIsValid = false;

            while (!clientInfoIsValid)
            {
                var addClientResult = ConsoleStatic.GetRemoteServerConnectionInfoFromUser();
                if (addClientResult.Failure)
                {
                    _log.Error($"Error: {addClientResult.Error} (GetRemoteServerInfoFromUserCommand.ExecuteAsync)");
                    Console.WriteLine(addClientResult.Error);
                    return Result.Fail(addClientResult.Error);
                }

                _newClient = addClientResult.Value;
                clientInfoIsValid = true;
            }

            lock (_state)
            {
                var requestServerInfo = new RequestAdditionalInfoFromRemoteServerCommand(_state, _newClient);
                var requestServerInfoResult = requestServerInfo.ExecuteAsync().GetAwaiter().GetResult();

                if (requestServerInfoResult.Success)
                {
                    _state.ClientInfo = _newClient.ConnectionInfo;
                    _state.ClientTransferFolderPath = _newClient.TransferFolder;
                    _state.ClientSelected = true;
                    //_state.IgnoreIncomingConnections = false;

                    _log.Info("Complete: GetRemoteServerInfoFromUserCommand.ExecuteAsync");
                    return Result.Ok();
                }

                _log.Error($"Error: {requestServerInfoResult.Error} (GetRemoteServerInfoFromUserCommand.ExecuteAsync)");
                _log.Info("Complete: GetRemoteServerInfoFromUserCommand.ExecuteAsync");
                Console.WriteLine(requestServerInfoResult.Error);
                return Result.Fail(requestServerInfoResult.Error);
            }
        }
    }
}
