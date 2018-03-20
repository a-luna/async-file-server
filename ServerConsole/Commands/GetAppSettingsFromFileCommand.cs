
namespace ServerConsole.Commands
{
    using System.Threading.Tasks;

    using AaronLuna.Common.Result;

    using Model;

    using TplSocketServer;

    class GetAppSettingsFromFileCommand : AppSettingsCommand
    {
        readonly string _settingsFilePath;

        public GetAppSettingsFromFileCommand(string itemText, string settingsFilePath)
            : base(itemText)
        {
            _settingsFilePath = settingsFilePath;
        }

        public override async Task<Result<AppSettings>> ExecuteAsync()
        {
            var settings = ConsoleStatic.InitializeAppSettings(_settingsFilePath);

            await Task.Delay(1);
            return Result.Ok(settings);
        }
    }
}
