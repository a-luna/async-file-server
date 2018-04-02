namespace ServerConsole.Commands.CompositeCommands
{
    using System.IO;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Logging;
    using AaronLuna.Common.Result;

    using Getters;
    using Setters;

    using TplSockets;

    class InitializeServerCommand : ICommand
    {
        readonly AppState _state;
        readonly string _settingsFilePath;
        readonly Logger _log = new Logger(typeof(InitializeServerCommand));

        public InitializeServerCommand(AppState state, string settingsFilePath)
        {
            _log.Info("Begin: Instantiate InitializeServerCommand");

            ReturnToParent = false;
            ItemText = "Initialize Local Server";

            _state = state;
            _settingsFilePath = settingsFilePath;

            _log.Info("Complete: Instantiate InitializeServerCommand");
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public async Task<Result> ExecuteAsync()
        {
            _log.Info("Begin: InitializeServerCommand.ExecuteAsync");

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
                _log.Error($"Error: {result.Error} (InitializeServerCommand.ExecuteAsync)");
                return Result.Fail("There was an error initializing the server");
            }

            var localIp = _state.MyLocalIpAddress;
            var port = _state.MyServerPort;

            _state.Server =
                new TplSocketServer(localIp, port)
                {
                    SocketSettings = _state.Settings.SocketSettings,
                    MyTransferFolderPath = _state.Settings.TransferFolderPath,
                    TransferUpdateInterval = _state.Settings.TransferUpdateInterval
                };

            _log.Info("Complete: InitializeServerCommand.ExecuteAsync");
            return Result.Ok();
        }
    }
}
