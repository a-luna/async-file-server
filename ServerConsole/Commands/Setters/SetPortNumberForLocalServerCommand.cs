namespace ServerConsole.Commands.Setters
{
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Result;

    class SetPortNumberForLocalServerCommand : ICommand<int>
    {
        public SetPortNumberForLocalServerCommand()
        {
            ReturnToParent = false;
            ItemText = "Set the port number used by this server";
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public async Task<CommandResult<int>> ExecuteAsync()
        {
            const string prompt = "Enter the port number this server will use to handle connections";
            var portNumber = ConsoleStatic.GetPortNumberFromUser(prompt, true);

            await Task.Delay(1);
            return new CommandResult<int>
            {
                ReturnToParent = ReturnToParent,
                Result = Result.Ok(portNumber)
            };
        }
    }
}
