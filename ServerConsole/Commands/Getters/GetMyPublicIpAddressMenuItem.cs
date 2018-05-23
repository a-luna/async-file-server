namespace ServerConsole.Commands.Getters
{
    using System;
    using System.Net;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Network;
    using AaronLuna.Common.Result;

    class GetMyPublicIpAddressMenuItem : IMenuItem
    {
        AppState _state;

        public GetMyPublicIpAddressMenuItem(AppState state)
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

            var retrievePublicIp =
                await NetworkUtilities.GetPublicIPv4AddressAsync().ConfigureAwait(false);

            var publicIp = IPAddress.Loopback;
            if (retrievePublicIp.Failure)
            {
                Console.WriteLine($"{Environment.NewLine}{retrievePublicIp.Error}{Environment.NewLine}");
                Console.WriteLine(notifyLanTrafficOnly);
                await Task.Delay(3000);
            }
            else
            {
                publicIp = retrievePublicIp.Value;
            }

            _state.UserEntryPublicIpAddress = publicIp;

            await Task.Delay(1);
            return Result.Ok();
        }
    }
}
