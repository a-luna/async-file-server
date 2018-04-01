namespace ServerConsole.Commands.ServerCommands
{
    using System.Net;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Logging;
    using AaronLuna.Common.Network;
    using AaronLuna.Common.Result;

    using TplSockets;

    class RequestPublicIpAddressCommand : ICommand
    {
        readonly AppState _state;
        readonly IPAddress _clientIp;
        readonly int _clientPort;
        readonly Logger _log = new Logger(typeof(RequestPublicIpAddressCommand));

        public RequestPublicIpAddressCommand(AppState state, IPAddress clientIp, int clientPort)
        {
            _log.Info("Begin: Instantiate RequestPublicIpAddressCommand");

            _state = state;
            _state.Server.EventOccurred += HandleServerEvent;

            _clientIp = clientIp;
            _clientPort = clientPort;

            ReturnToParent = false;
            ItemText = "Request public IP address";

            _log.Info("Complete: Instantiate RequestPublicIpAddressCommand");
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public async Task<Result> ExecuteAsync()
        {
            _log.Info("Begin: RequestPublicIpAddressCommand.ExecuteAsync");

            _state.WaitingForPublicIpResponse = true;
            _state.ClientResponseIsStalled = false;
            _state.ClientInfo.PublicIpAddress = IPAddress.None;

            var sendIpRequestResult =
                await _state.Server.RequestPublicIpAsync(
                        _clientIp.ToString(),
                        _clientPort)
                    .ConfigureAwait(false);

            if (sendIpRequestResult.Failure)
            {
                var error = $"Error requesting public IP address from new client:\n{sendIpRequestResult.Error}";
                _log.Error($"{error} (RequestPublicIpAddressCommand.ExecuteAsync)");
                return Result.Fail(error);
            }

            while (_state.WaitingForPublicIpResponse) { }

            _log.Info("Complete: RequestPublicIpAddressCommand.ExecuteAsync");
            _state.Server.EventOccurred -= HandleServerEvent;

            return Result.Ok();
        }
        
        void HandleServerEvent(object sender, ServerEvent serverEvent)
        {
            switch (serverEvent.EventType)
            {
                case EventType.ReceivedPublicIpAddress:

                    _state.WaitingForPublicIpResponse = false;
                    _state.ClientInfo.PublicIpAddress = 
                        Network.ParseSingleIPv4Address(serverEvent.PublicIpAddress).Value;

                    break;
            }
        }
    }
}

