﻿namespace ServerConsole.Commands.ServerCommands
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
                        _state.RemoteServerInfo.SessionIpAddress.ToString(),
                        _state.RemoteServerInfo.Port)
                    .ConfigureAwait(false);

            if (sendIpRequestResult.Failure)
            {
                var error = $"Error requesting public IP address from new client:\n{sendIpRequestResult.Error}";
                return Result.Fail(error);
            }

            while (_state.WaitingForPublicIpResponse) { }            

            return Result.Ok();
        }
        
        void HandleServerEvent(object sender, ServerEvent serverEvent)
        {
            switch (serverEvent.EventType)
            {
                
            }
        }
    }
}

