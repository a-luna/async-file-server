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
            _state.MyInfo.LocalIpAddress = ConsoleStatic.GetLocalIpAddress();

            await Task.Delay(1);
            return Result.Ok();
        }
    }
}
