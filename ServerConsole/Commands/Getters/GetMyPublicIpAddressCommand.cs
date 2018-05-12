﻿namespace ServerConsole.Commands.Getters
{
    using System;
    using System.Net;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Network;
    using AaronLuna.Common.Result;

    class GetMyPublicIpAddressCommand : ICommand
    {
        AppState _state;

        public GetMyPublicIpAddressCommand(AppState state)
        {
            ReturnToParent = false;
            ItemText = "Refresh the value of this server's external/public IP address";

            _state = state;
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public async Task<Result> ExecuteAsync()
        {
            const string notifyLanTrafficOnly = 
                "Unable to determine public IP address, this server will only be able " +
                "to communicate with machines in the same local network.";

            Console.Clear();
            var retrievePublicIp =
                await NetworkUtilities.GetPublicIPv4AddressAsync().ConfigureAwait(false);

            var publicIp = IPAddress.Loopback;
            if (retrievePublicIp.Failure)
            {
                Console.WriteLine(notifyLanTrafficOnly);
            }
            else
            {
                publicIp = retrievePublicIp.Value;
            }

            _state.LocalServerInfo.PublicIpAddress = publicIp;

            await Task.Delay(1);
            return Result.Ok();
        }
    }
}
