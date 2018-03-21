namespace ServerConsole.Commands.CompositeCommands
{
    using System;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Result;
    using TplSocketServer;

    class GetRemoteServerInfoFromUserCommand : ICommand<RemoteServer>
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

        public async Task<CommandResult<RemoteServer>> ExecuteAsync()
        {
            _newClient = new RemoteServer();
            var clientInfoIsValid = false;

            while (!clientInfoIsValid)
            {
                var addClientResult = ConsoleStatic.GetRemoteServerConnectionInfoFromUser();
                if (addClientResult.Failure)
                {
                    Console.WriteLine(addClientResult.Error);
                    return new CommandResult<RemoteServer>
                    {
                        ReturnToParent = ReturnToParent,
                        Result = addClientResult
                    };
                }

                _newClient = addClientResult.Value;
                clientInfoIsValid = true;
            }

            var requestServerInfo = new RequestAdditionalInfoFromRemoteServerCommand(_state, _newClient);
            var requestServerInfoCommandResult = await requestServerInfo.ExecuteAsync();
            var requestServerInfoResult = requestServerInfoCommandResult.Result;

            if (requestServerInfoResult.Failure)
            {
                Console.WriteLine(requestServerInfoResult.Error);
                return new CommandResult<RemoteServer>
                {
                    ReturnToParent = ReturnToParent,
                    Result = Result.Fail<RemoteServer>(requestServerInfoResult.Error)
                };
            }

            _newClient = requestServerInfoResult.Value;

            await Task.Delay(1);
            return new CommandResult<RemoteServer>
            {
                ReturnToParent = ReturnToParent,
                Result = Result.Ok(_newClient)
            };
        }
    }
}
