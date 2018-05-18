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
            var setPortResult = Result.Ok();

            if (_state.Settings.LocalPort == 0)
            {
                setPortResult = await setPortCommand.ExecuteAsync();
                _state.Settings.LocalPort = _state.UserEntryLocalServerPort;
            }

            var setCidrIpCommand = new SetMyCidrIpCommand(_state);
            var setCidrIpResult = Result.Ok();

            if (string.IsNullOrEmpty(_state.Settings.LocalNetworkCidrIp))
            {
                setCidrIpResult = await setCidrIpCommand.ExecuteAsync();
                _state.Settings.LocalNetworkCidrIp = _state.UserEntryLocalNetworkCidrIp;
            }

            var getLocalIpCommand = new GetMyLocalIpAddressCommand(_state);
            var getLocalIpResult = await getLocalIpCommand.ExecuteAsync();

            var getPublicIpCommand = new GetMyPublicIpAddressCommand(_state);
            var getPublicIpResult = await getPublicIpCommand.ExecuteAsync();

            var result = Result.Combine(setPortResult, setCidrIpResult, getLocalIpResult, getPublicIpResult);
            if (result.Failure)
            {
                _log.Error($"Error: {result.Error} (InitializeServerCommand.ExecuteAsync)");
                return Result.Fail("There was an error initializing the server");
            }

            var port = _state.Settings.LocalPort;
            var localIp = _state.UserEntryLocalIpAddress;
            var publicIp = _state.UserEntryPublicIpAddress;

            _state.LocalServer.Initialize(localIp, port);
            _state.LocalServer.SocketSettings = _state.Settings.SocketSettings;
            _state.LocalServer.TransferUpdateInterval = _state.Settings.FileTransferUpdateInterval;
            _state.LocalServer.Info.PublicIpAddress = publicIp;
            _state.LocalServer.Info.TransferFolder = _state.Settings.LocalServerFolderPath;

            return Result.Ok();
        }

        public ServerSettings InitializeAppSettings(string settingsFilePath)
        {
            var defaultTransferFolderPath
                = $"{Directory.GetCurrentDirectory()}{Path.DirectorySeparatorChar}transfer";

            var settings = new ServerSettings
            {
                MaxDownloadAttempts = 3,
                LocalServerFolderPath = defaultTransferFolderPath,
                FileTransferUpdateInterval = 0.0025f
            };

            if (!File.Exists(settingsFilePath)) return settings;

            var readFromFileResult = ServerSettings.ReadFromFile(settingsFilePath);
            if (readFromFileResult.Success)
            {
                settings = readFromFileResult.Value;
                _state.UserEntryLocalServerPort = settings.LocalPort;
                _state.UserEntryLocalNetworkCidrIp = settings.LocalNetworkCidrIp;
            }
            else
            {
                Console.WriteLine(readFromFileResult.Error);
            }

            return settings;
        }
    }
}
