namespace ServerConsole.Commands.ServerCommands
{
    using System;
    using System.Net;
    using System.Threading;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Logging;
    using AaronLuna.Common.Result;

    using TplSocketServer;

    class RequestTransferFolderCommand : ICommand
    {
        const string ConnectionRefusedAdvice =
            "\nPlease verify that the port number on the client server is properly opened, this could entail modifying firewall or port forwarding settings depending on the operating system.";

        readonly AppState _state;
        readonly IPAddress _clientIp;
        readonly int _clientPort;

        readonly Logger _log = new Logger(typeof(RequestTransferFolderCommand));

        public RequestTransferFolderCommand(AppState state, IPAddress clientIp, int clientPort)
        {
            _log.Info("Begin: Instantiate RequestTransferFolderCommand");

            _state = state;
            _state.Server.EventOccurred += HandleServerEvent;

            _clientIp = clientIp;
            _clientPort = clientPort;

            ReturnToParent = false;
            ItemText = "Request transfer folder path";

            _log.Info("Complete: Instantiate RequestTransferFolderCommand");
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public async Task<Result> ExecuteAsync()
        {
            _log.Info("Begin: RequestTransferFolderCommand.ExecuteAync");

            _state.WaitingForTransferFolderResponse = true;
            _state.ClientResponseIsStalled = false;
            _state.ClientTransferFolderPath = string.Empty;

            var sendFolderRequestResult =
                await _state.Server.RequestTransferFolderPathAsync(
                        _clientIp.ToString(),
                        _clientPort)
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

            _log.Info("Complete: RequestTransferFolderCommand.ExecuteAync");
            _state.Server.EventOccurred -= HandleServerEvent;

            return Result.Ok();
        }

        void HandleServerEvent(object sender, ServerEvent serverEvent)
        {
            switch (serverEvent.EventType)
            {
                case EventType.ReadTransferFolderResponseComplete:
                    _state.WaitingForTransferFolderResponse = false;
                    _state.ClientTransferFolderPath = serverEvent.RemoteFolder;
                    break;
            }
        }
    }
}
