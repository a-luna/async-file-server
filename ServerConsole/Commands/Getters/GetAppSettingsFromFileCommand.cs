namespace ServerConsole.Commands.Getters
{
    using System;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Result;

    class GetAppSettingsFromFileCommand : ICommand
    {
        AppState _state;
        readonly string _settingsFilePath;

        public GetAppSettingsFromFileCommand(AppState state, string settingsFilePath)
        {
            ReturnToParent = false;
            ItemText = "Read server settings from file";

            _state = state;
            _settingsFilePath = settingsFilePath;
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public async Task<Result> ExecuteAsync()
        {
            Console.Clear();

            _state.Settings = ConsoleStatic.InitializeAppSettings(_settingsFilePath);

            await Task.Delay(1);
            return Result.Ok();
        }
    }
}
