using System.Net;

namespace ServerConsole.Commands.Getters
{
    using System;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Network;
    using AaronLuna.Common.Result;

    class GetMyLocalIpAddressCommand : ICommand
    {
        AppState _state;

        public GetMyLocalIpAddressCommand(AppState state)
        {
            ReturnToParent = false;
            ItemText = "Refresh the value of this server's local/private IP address";

            _state = state;
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public async Task<Result> ExecuteAsync()
        {
            Console.Clear();
            var cidrIp = _state.Settings.LocalNetworkCidrIp;
            var getLocalIpResult = NetworkUtilities.GetLocalIPv4Address(cidrIp);

            if (getLocalIpResult.Failure)
            {
                const string useLoopbackIpPrompt =
                    "Unable to determine the local IP address for this machine, please " +
                    "ensure that the CIDR IP address is correct for your LAN.\nWould you " +
                    "like to use 127.0.0.1 (loopback) as the IP address of this server? " +
                    "(you will only be able to communicate with other servers running on " +
                    "the same local machine, this is only useful for testing)";

                var useLoopback = SharedFunctions.PromptUserYesOrNo(useLoopbackIpPrompt);
                if (!useLoopback)
                {
                    return Result.Fail(getLocalIpResult.Error);
                }

                _state.UserEntryLocalIpAddress = IPAddress.Loopback;
            }

            _state.UserEntryLocalIpAddress = getLocalIpResult.Value;

            await Task.Delay(1);
            return Result.Ok();
        }
    }
}
