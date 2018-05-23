namespace ServerConsole.Commands.ServerCommands
{
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Result;

    class RequestTransferFolderMenuItem : IMenuItem
    {   
        readonly AppState _state;

        public RequestTransferFolderMenuItem(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = "Request transfer folder path";
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public async Task<Result> ExecuteAsync()
        {
            var remoteIp = _state.SelectedServer.SessionIpAddress;
            var remotePort = _state.SelectedServer.Port;

            _state.WaitingForTransferFolderResponse = true;

            var sendFolderRequestResult =
                await _state.LocalServer.RequestTransferFolderPathAsync(
                        remoteIp,
                        remotePort)
                    .ConfigureAwait(false);

            if (sendFolderRequestResult.Failure)
            {   
                return Result.Fail(sendFolderRequestResult.Error);
            }

            while (_state.WaitingForTransferFolderResponse) { }

            return Result.Ok();
        }
    }
}
