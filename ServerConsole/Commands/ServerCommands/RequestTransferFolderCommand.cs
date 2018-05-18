namespace ServerConsole.Commands.ServerCommands
{
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Logging;
    using AaronLuna.Common.Result;

    using TplSockets;

    class RequestTransferFolderCommand : ICommand
    {
        const string ConnectionRefusedAdvice =
            "\nPlease verify that the port number on the client server is properly opened, this could entail modifying firewall or port forwarding settings depending on the operating system.";

        readonly AppState _state;
        readonly Logger _log = new Logger(typeof(RequestTransferFolderCommand));

        public RequestTransferFolderCommand(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = "Request transfer folder path";
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public async Task<Result> ExecuteAsync()
        {
            _state.WaitingForTransferFolderResponse = true;
            _state.RemoteServerInfo.TransferFolder = string.Empty;

            var sendFolderRequestResult =
                await _state.LocalServer.RequestTransferFolderPathAsync(
                        _state.RemoteServerInfo.SessionIpAddress.ToString(),
                        _state.RemoteServerInfo.Port)
                    .ConfigureAwait(false);

            if (sendFolderRequestResult.Failure)
            {
                var userHint = string.Empty;
                if (sendFolderRequestResult.Error.Contains("Connection refused"))
                {
                    userHint = ConnectionRefusedAdvice;
                }

                _log.Error($"Error: {sendFolderRequestResult.Error} (RequestTransferFolderCommand.ExecuteAsync)");
                return Result.Fail($"{sendFolderRequestResult.Error}{userHint}");
            }

            while (_state.WaitingForTransferFolderResponse) { }

            return Result.Ok();
        }
    }
}
