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

    class InitializeServerMenuItem : IMenuItem
    {
        readonly AppState _state;
        readonly string _settingsFilePath;
        readonly Logger _log = new Logger(typeof(InitializeServerMenuItem));

        public InitializeServerMenuItem(AppState state, string settingsFilePath)
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
            InitializeSettings();
            var settingsChanged = false;

            var setPortCommand = new SetMyPortNumberMenuItem(_state);
            var setPortResult = Result.Ok();

            if (_state.Settings.LocalPort == 0)
            {
                setPortResult = await setPortCommand.ExecuteAsync();
                _state.Settings.LocalPort = _state.UserEntryLocalServerPort;
                settingsChanged = true;
            }

            var setCidrIpCommand = new SetMyCidrIpMenuItem(_state);
            var setCidrIpResult = Result.Ok();

            if (string.IsNullOrEmpty(_state.Settings.LocalNetworkCidrIp))
            {
                setCidrIpResult = await setCidrIpCommand.ExecuteAsync();
                _state.Settings.LocalNetworkCidrIp = _state.UserEntryLocalNetworkCidrIp;
                settingsChanged = true;
            }

            var getLocalIpCommand = new GetMyLocalIpAddressMenuItem(_state);
            var getLocalIpResult = await getLocalIpCommand.ExecuteAsync();

            var getPublicIpCommand = new GetMyPublicIpAddressMenuItem(_state);
            var getPublicIpResult = await getPublicIpCommand.ExecuteAsync();

            var result = Result.Combine(setPortResult, setCidrIpResult, getLocalIpResult, getPublicIpResult);
            if (result.Failure)
            {
                _log.Error($"Error: {result.Error} (InitializeServerMenuItem.ExecuteAsync)");
                return Result.Fail("There was an error initializing the server");
            }

            var port = _state.Settings.LocalPort;
            var cidrIp = _state.Settings.LocalNetworkCidrIp;
            var localIp = _state.UserEntryLocalIpAddress;
            var publicIp = _state.UserEntryPublicIpAddress;

            _state.LocalServer.Initialize(localIp, cidrIp, port);
            _state.LocalServer.SocketSettings = _state.Settings.SocketSettings;
            _state.LocalServer.TransferUpdateInterval = _state.Settings.FileTransferUpdateInterval;
            _state.LocalServer.Info.PublicIpAddress = publicIp;
            _state.LocalServer.Info.TransferFolder = _state.Settings.LocalServerFolderPath;

            if (settingsChanged)
            {
                ServerSettings.SaveToFile(_state.Settings, _state.SettingsFilePath);
            }

            return Result.Ok();
        }

        void InitializeSettings()
        {
            _state.SettingsFile = new FileInfo(_settingsFilePath);

            var readSettingsFileResult = ServerSettings.ReadFromFile(_state.SettingsFilePath);
            if (readSettingsFileResult.Failure)
            {
                Console.WriteLine(readSettingsFileResult.Error);
            }

            _state.Settings = readSettingsFileResult.Value;
            _state.UserEntryLocalServerPort = _state.Settings.LocalPort;
            _state.UserEntryLocalNetworkCidrIp = _state.Settings.LocalNetworkCidrIp;
        }
    }
}
