namespace ServerConsole.Commands.CompositeCommands
{
    using System;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Result;
    using TplSocketServer;

    class GetRemoteServerInfoFromUserCommand : ICommand
    {
        AppState _state;
        RemoteServer _newClient;
        
        public GetRemoteServerInfoFromUserCommand(AppState state)
        {
            ReturnToParent = false;
            ItemText = "Add new client";

            _state = state;
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public async Task<Result> ExecuteAsync()
        {
            _newClient = new RemoteServer();
            var clientInfoIsValid = false;

            while (!clientInfoIsValid)
            {
                var addClientResult = ConsoleStatic.GetRemoteServerConnectionInfoFromUser();
                if (addClientResult.Failure)
                {
                    Console.WriteLine(addClientResult.Error);
                    return Result.Fail(addClientResult.Error);
                }

                _newClient = addClientResult.Value;
                clientInfoIsValid = true;
            }

            var requestServerInfo = new RequestAdditionalInfoFromRemoteServerCommand(_state, _newClient);
            var requestServerInfoResult = await requestServerInfo.ExecuteAsync();

            if (requestServerInfoResult.Success)
            {
                _state.ClientInfo = _newClient.ConnectionInfo;
                _state.ClientTransferFolderPath = _newClient.TransferFolder;
                _state.ClientSelected = true;

                return Result.Ok();
            }

            Console.WriteLine(requestServerInfoResult.Error);
            return Result.Fail(requestServerInfoResult.Error);

        }
    }
}
