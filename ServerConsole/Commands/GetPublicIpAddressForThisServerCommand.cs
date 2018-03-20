namespace ServerConsole.Commands
{
    using System.Net;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Result;

    class GetPublicIpAddressForThisServerCommand : IpAddressCommand
    {
        public GetPublicIpAddressForThisServerCommand(string itemText)
            : base(itemText) { }

        public override async Task<Result<IPAddress>> ExecuteAsync()
        {
            var publicIp = await ConsoleStatic.GetPublicIpAddressForLocalMachineAsync();

            await Task.Delay(1);
            return Result.Ok(publicIp);
        }
    }
}
