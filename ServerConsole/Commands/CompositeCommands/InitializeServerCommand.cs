namespace ServerConsole.Commands.CompositeCommands
{
    using System;
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
            _state.Settings = InitializeAppSettings(_state.SettingsFilePath);

            var setPortCommand = new SetMyPortNumberCommand(_state);
            Result setPortResult = Result.Ok();

            if (_state.Settings.LocalPort == 0)
            {
                setPortResult = await setPortCommand.ExecuteAsync();
            }

            var getLocalIpCommand = new GetMyLocalIpAddressCommand(_state);
            var getLocalIpResult = await getLocalIpCommand.ExecuteAsync();

            var getPublicIpCommand = new GetMyPublicIpAddressCommand(_state);
            var getPublicIpResult = await getPublicIpCommand.ExecuteAsync();

            var result = Result.Combine(setPortResult, getLocalIpResult, getPublicIpResult);
            if (result.Failure)
            {
                _log.Error($"Error: {result.Error} (InitializeServerCommand.ExecuteAsync)");
                return Result.Fail("There was an error initializing the server");
            }

            var localIp = _state.MyLocalIpAddress;
            var port = _state.MyServerPort;

            _state.LocalServer.InitializeServer(localIp, port);
            _state.LocalServer.SocketSettings = _state.Settings.SocketSettings;
            _state.LocalServer.MyTransferFolderPath = _state.Settings.LocalServerFolderPath;
            _state.LocalServer.TransferUpdateInterval = _state.Settings.FileTransferUpdateInterval;

            _log.Info("Complete: InitializeServerCommand.ExecuteAsync");
            return Result.Ok();
        }

        public static AppSettings InitializeAppSettings(string settingsFilePath)
        {
            var defaultTransferFolderPath
                = $"{Directory.GetCurrentDirectory()}{Path.DirectorySeparatorChar}transfer";

            var settings = new AppSettings
            {
                MaxDownloadAttempts = 3,
                LocalServerFolderPath = defaultTransferFolderPath,
                FileTransferUpdateInterval = 0.0025f
            };

            if (!File.Exists(settingsFilePath)) return settings;

            var deserialized = AppSettings.Deserialize(settingsFilePath);
            if (deserialized.Success)
            {
                settings = deserialized.Value;
            }
            else
            {
                Console.WriteLine(deserialized.Error);
            }

            return settings;
        }
    }
}
