namespace ServerConsole.Commands.ServerCommands
{
    using System.Net;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Logging;
    using AaronLuna.Common.Result;

    using TplSockets;

    class RequestPublicIpAddressCommand : ICommand
    {
        readonly AppState _state;
        readonly Logger _log = new Logger(typeof(RequestPublicIpAddressCommand));

        public RequestPublicIpAddressCommand(AppState state)
        {
            _state = state;
            _state.LocalServer.EventOccurred += HandleServerEvent;

            ReturnToParent = false;
            ItemText = "Request public IP address";
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public async Task<Result> ExecuteAsync()
        {
            _state.WaitingForPublicIpResponse = true;
            _state.RemoteServerInfo.PublicIpAddress = IPAddress.None;

            var sendIpRequestResult =
                await _state.LocalServer.RequestPublicIpAsync(
                        _state.ClientSessionIpAddress.ToString(),
                        _state.ClientServerPort)
                    .ConfigureAwait(false);

            if (sendIpRequestResult.Failure)
            {
                var error = $"Error requesting public IP address from new client:\n{sendIpRequestResult.Error}";
                return Result.Fail(error);
            }

            while (_state.WaitingForPublicIpResponse) { }            
            _state.LocalServer.EventOccurred -= HandleServerEvent;

            return Result.Ok();
        }
        
        void HandleServerEvent(object sender, ServerEvent serverEvent)
        {
            switch (serverEvent.EventType)
            {
                case EventType.ReceivedPublicIpAddress:
                    _state.WaitingForPublicIpResponse = false;
                    _state.RemoteServerInfo.PublicIpAddress = serverEvent.PublicIpAddress;
                    break;
            }
        }
    }
}

