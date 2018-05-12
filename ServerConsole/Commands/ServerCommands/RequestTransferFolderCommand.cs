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
            _state.LocalServer.EventOccurred += HandleServerEvent;

            ReturnToParent = false;
            ItemText = "Request transfer folder path";
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public async Task<Result> ExecuteAsync()
        {
            _state.WaitingForTransferFolderResponse = true;
            _state.ClientTransferFolderPath = string.Empty;

            var sendFolderRequestResult =
                await _state.LocalServer.RequestTransferFolderPathAsync(
                        _state.ClientSessionIpAddress.ToString(),
                        _state.ClientServerPort)
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
            _state.LocalServer.EventOccurred -= HandleServerEvent;

            return Result.Ok();
        }

        void HandleServerEvent(object sender, ServerEvent serverEvent)
        {
            switch (serverEvent.EventType)
            {
                case EventType.ReceivedTransferFolderPath:
                    _state.WaitingForTransferFolderResponse = false;
                    _state.ClientTransferFolderPath = serverEvent.RemoteFolder;
                    break;
            }
        }
    }
}
