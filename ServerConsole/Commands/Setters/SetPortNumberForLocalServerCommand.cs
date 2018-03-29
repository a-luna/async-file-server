namespace ServerConsole.Commands.Setters
{
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Result;

    class SetPortNumberForLocalServerCommand : ICommand
    {
        AppState _state;

        public SetPortNumberForLocalServerCommand(AppState state)
        {
            ReturnToParent = false;
            ItemText = "Set the port number used by this server";

            _state = state;
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public async Task<Result> ExecuteAsync()
        {
            const string prompt = "Enter the port number this server will use to handle connections";
            _state.MyInfo.Port = ConsoleStatic.GetPortNumberFromUser(prompt, true);
            
            await Task.Delay(1);
            return Result.Ok();
        }
    }
}
