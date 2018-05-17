namespace ServerConsole.Commands.CompositeCommands
{
    using System;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Logging;
    using AaronLuna.Common.Result;

    class GetRemoteServerInfoFromUserCommand : ICommand
    {
        readonly AppState _state;
        readonly Logger _log = new Logger(typeof(GetRemoteServerInfoFromUserCommand));

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
            var clientInfoIsValid = false;

            while (!clientInfoIsValid)
            {
                var addClientResult = SharedFunctions.GetRemoteServerConnectionInfoFromUser();
                if (addClientResult.Failure)
                {
                    _log.Error($"Error: {addClientResult.Error} (GetRemoteServerInfoFromUserCommand.ExecuteAsync)");
                    Console.WriteLine(addClientResult.Error);
                    return Result.Fail(addClientResult.Error);
                }

                _state.RemoteServerInfo = addClientResult.Value;
                clientInfoIsValid = true;
            }

            var requestServerInfo = new RequestAdditionalInfoFromRemoteServerCommand(_state);
            var requestServerInfoResult = await requestServerInfo.ExecuteAsync();

            if (requestServerInfoResult.Success)
            {
                _state.ClientSelected = true;

                _log.Info("Complete: GetRemoteServerInfoFromUserCommand.ExecuteAsync");
                return Result.Ok();
            }

            _log.Error($"Error: {requestServerInfoResult.Error} (GetRemoteServerInfoFromUserCommand.ExecuteAsync)");
            Console.WriteLine(requestServerInfoResult.Error);
            return Result.Fail(requestServerInfoResult.Error);
        }
    }
}
