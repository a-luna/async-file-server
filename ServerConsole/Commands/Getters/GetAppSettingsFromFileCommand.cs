namespace ServerConsole.Commands.Getters
{
    using System;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Result;

    using TplSocketServer;

    class GetAppSettingsFromFileCommand : ICommand<AppSettings>
    {
        readonly string _settingsFilePath;

        public GetAppSettingsFromFileCommand(string settingsFilePath)
        {
            ReturnToParent = false;
            ItemText = "Read server settings from file";

            _settingsFilePath = settingsFilePath;
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public async Task<CommandResult<AppSettings>> ExecuteAsync()
        {
            Console.Clear();

            var settings = ConsoleStatic.InitializeAppSettings(_settingsFilePath);

            await Task.Delay(1);

            return new CommandResult<AppSettings>
            {
                ReturnToParent = ReturnToParent,
                Result = Result.Ok(settings)
            };
        }
    }
}
