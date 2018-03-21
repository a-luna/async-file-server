namespace ServerConsole.Commands.Getters
{
    using System;
    using System.Net;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Result;

    class GetLocalIpAddressForThisServerCommand : ICommand<IPAddress>
    {
        public GetLocalIpAddressForThisServerCommand()
        {
            ReturnToParent = false;
            ItemText = "Get local IP address for this machine";
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public async Task<CommandResult<IPAddress>> ExecuteAsync()
        {
            Console.Clear();
            var localIp = ConsoleStatic.GetLocalIpAddress();

            await Task.Delay(1);
            return new CommandResult<IPAddress>
            {
                ReturnToParent = ReturnToParent,
                Result = Result.Ok(localIp)
            };
        }
    }
}
