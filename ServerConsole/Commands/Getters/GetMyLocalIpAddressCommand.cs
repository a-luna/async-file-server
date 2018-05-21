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
            //Console.Clear();
            var cidrIp = _state.Settings.LocalNetworkCidrIp;
            var getLocalIpResult = NetworkUtilities.GetLocalIPv4Address(cidrIp);

            if (getLocalIpResult.Failure)
            {
                var useLoopback = SharedFunctions.PromptUserYesOrNo(Resources.Warning_UseLoopbackIp);
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
