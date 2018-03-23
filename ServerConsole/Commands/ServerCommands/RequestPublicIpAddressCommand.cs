namespace ServerConsole.Commands.ServerCommands
{
    using System;
    using System.Net;
    using System.Threading;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Network;
    using AaronLuna.Common.Result;

    using TplSocketServer;

    class RequestPublicIpAddressCommand : ICommand
    {
        readonly AppState _state;
        readonly IPAddress _clientIp;
        readonly int _clientPort;

        public RequestPublicIpAddressCommand(AppState state, IPAddress clientIp, int clientPort)
        {
            _state = state;
            _state.Server.EventOccurred += HandleServerEvent;

            _clientIp = clientIp;
            _clientPort = clientPort;

            ReturnToParent = false;
            ItemText = "Request public IP address";
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public async Task<Result> ExecuteAsync()
        {
            _state.WaitingForPublicIpResponse = true;
            _state.ClientResponseIsStalled = false;
            _state.ClientInfo.PublicIpAddress = IPAddress.None;

            var sendIpRequestResult =
                await _state.Server.RequestPublicIpAsync(
                        _clientIp.ToString(),
                        _clientPort,
                        _state.MyInfo.LocalIpAddress.ToString(),
                        _state.MyInfo.Port,
                        new CancellationToken())
                    .ConfigureAwait(false);

            if (sendIpRequestResult.Failure)
            {
                var error = $"Error requesting transfer folder path from new client:\n{sendIpRequestResult.Error}";
                return Result.Fail(error);
            }

            var twoSecondTimer = new Timer(HandleTimeout, true, 2000, Timeout.Infinite);

            while (_state.WaitingForPublicIpResponse)
            {
                if (_state.ClientResponseIsStalled)
                {
                    twoSecondTimer.Dispose();
                    throw new TimeoutException();
                }
            }

            return Result.Ok();
        }

        void HandleTimeout(object state)
        {
            _state.ClientResponseIsStalled = true;
        }

        void HandleServerEvent(object sender, ServerEvent eventInfo)
        {
            switch (eventInfo.EventType)
            {
                case EventType.ReadPublicIpResponseComplete:

                    _state.WaitingForPublicIpResponse = false;
                    _state.ClientInfo.PublicIpAddress = 
                        Network.ParseSingleIPv4Address(eventInfo.PublicIpAddress).Value;

                    break;
            }
        }
    }
}
