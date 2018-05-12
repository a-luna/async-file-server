namespace ServerConsole.Commands.Setters
{
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Result;

    using TplSockets;

    class SetMyPortNumberCommand : ICommand
    {
        readonly AppState _state;

        public SetMyPortNumberCommand(AppState state)
        {
            ReturnToParent = false;
            ItemText = "Set listening port number";

            _state = state;
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public async Task<Result> ExecuteAsync()
        {
            const string prompt = "Enter the port number where this server will listen for incoming connections";
            _state.Settings.LocalPort = SharedFunctions.GetPortNumberFromUser(prompt, true);
            _state.LocalServerInfo.Port = _state.Settings.LocalPort;

            AppSettings.SaveToFile(_state.Settings, _state.SettingsFilePath);
            await Task.Delay(1);
            return Result.Ok();
        }
    }
}
