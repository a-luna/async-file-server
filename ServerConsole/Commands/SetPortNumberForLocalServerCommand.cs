namespace ServerConsole.Commands
{
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Result;

    class SetPortNumberForLocalServerCommand : IntCommand
    {
        public SetPortNumberForLocalServerCommand(string itemText) : base(itemText) { }

        public override async Task<Result<int>> ExecuteAsync()
        {
            const string prompt = "Enter the port number this server will use to handle connections";
            var portNumber = ConsoleStatic.GetPortNumberFromUser(prompt, true);

            await Task.Delay(1);
            return Result.Ok(portNumber);
        }
    }
}
