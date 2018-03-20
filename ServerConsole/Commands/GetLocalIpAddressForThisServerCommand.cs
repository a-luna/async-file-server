namespace ServerConsole.Commands
{
    using System.Net;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Result;

    class GetLocalIpAddressForThisServerCommand : IpAddressCommand
    {
        public GetLocalIpAddressForThisServerCommand(string itemText)
            : base(itemText) { }

        public override async Task<Result<IPAddress>> ExecuteAsync()
        {
            var localIp = ConsoleStatic.GetLocalIpAddress();

            await Task.Delay(1);
            return Result.Ok(localIp);
        }
    }
}
