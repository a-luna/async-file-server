using System.IO;

namespace ServerConsole.Commands.CompositeCommands
{
    using System.Threading.Tasks;
    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Result;
    using Getters;
    using Setters;
    using TplSocketServer;

    class InitializeServerCommand : ICommand
    {
        readonly AppState _state;
        readonly string _settingsFilePath;

        public InitializeServerCommand(AppState state, string settingsFilePath)
        {
            ReturnToParent = false;
            ItemText = "Initialize Local Server";

            _state = state;
            _settingsFilePath = settingsFilePath;
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public async Task<Result> ExecuteAsync()
        {
            _state.SettingsFile = new FileInfo(_settingsFilePath);

            var getSettingsCommand = new GetAppSettingsFromFileCommand(_state, _settingsFilePath);
            var getSettingsResult = await getSettingsCommand.ExecuteAsync();
            
            var setPortCommand = new SetPortNumberForLocalServerCommand(_state);
            var setPortResult = await setPortCommand.ExecuteAsync();

            var getLocalIpCommand = new GetMyLocalIpAddressCommand(_state);
            var getLocalIpResult = await getLocalIpCommand.ExecuteAsync();

            var getPublicIpCommand = new GetMyPublicIpAddressCommand(_state);
            var getPublicIpResult = await getPublicIpCommand.ExecuteAsync();

            var result = Result.Combine(getSettingsResult, setPortResult, getLocalIpResult, getPublicIpResult);
            if (result.Failure)
            {
                return Result.Fail("There was an error initializing the server");
            }

            var localIp = _state.MyInfo.LocalIpAddress;
            var port = _state.MyInfo.Port;

            _state.Server =
                new TplSocketServer(localIp, port)
                {
                    SocketSettings = _state.Settings.SocketSettings,
                    TransferFolderPath = _state.Settings.TransferFolderPath,
                    TransferUpdateInterval = _state.Settings.TransferUpdateInterval,
                    LoggingEnabled = true
                };

            return Result.Ok();
        }
    }
}
