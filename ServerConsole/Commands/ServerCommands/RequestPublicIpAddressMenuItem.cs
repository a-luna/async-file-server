namespace ServerConsole.Commands.ServerCommands
{
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Result;

    class RequestPublicIpAddressMenuItem : IMenuItem
    {
        readonly AppState _state;
        
        public RequestPublicIpAddressMenuItem(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = "Request public IP address";
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public async Task<Result> ExecuteAsync()
        {
            var remoteIp = _state.SelectedServer.SessionIpAddress;
            var remotePort = _state.SelectedServer.Port;

            _state.WaitingForPublicIpResponse = true;

            var sendIpRequestResult =
                await _state.LocalServer.RequestPublicIpAsync(
                        remoteIp,
                        remotePort)
                    .ConfigureAwait(false);

            if (sendIpRequestResult.Failure)
            {
                var error = $"Error requesting public IP address from new client:\n{sendIpRequestResult.Error}";
                return Result.Fail(error);
            }

            while (_state.WaitingForPublicIpResponse) { }

            return Result.Ok();
        }
    }
}

