using AaronLuna.Common.Network;

namespace ServerConsole.Commands.Getters
{
    using System;
    using System.Net;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Result;

    class GetMyLocalIpAddressCommand : ICommand
    {
        AppState _state;

        public GetMyLocalIpAddressCommand(AppState state)
        {
            ReturnToParent = false;
            ItemText = "Get local IP address for this machine";

            _state = state;
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public async Task<Result> ExecuteAsync()
        {
            Console.Clear();
            var getLocalIpResult = NetworkUtilities.GetLocalIpAddress("192.168.2.1/24");
            if (getLocalIpResult.Failure)
            {
                return Result.Fail(getLocalIpResult.Error);
            }

            _state.MyInfo.LocalIpAddress = getLocalIpResult.Value;

            await Task.Delay(1);
            return Result.Ok();
        }
    }
}
