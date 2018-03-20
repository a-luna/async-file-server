namespace ServerConsole.Commands
{
    using System.Threading.Tasks;

    using AaronLuna.Common.Result;

    using Model;

    using TplSocketServer;

    class InitializeServerCommand : TplSocketServerCommand
    {
        readonly AppSettings _settings;

        public InitializeServerCommand(string itemText, AppSettings settings) 
            : base(itemText)
        {
            _settings = settings;
        }

        public override async Task<Result<TplSocketServer>> ExecuteAsync()
        {
            var setPortCommand = new SetPortNumberForLocalServerCommand("Get Port Number for Local Server");
            var setPortResult = await setPortCommand.ExecuteAsync();

            var getLocalIpCommand = new GetLocalIpAddressForThisServerCommand("Get local IP address for this machine");
            var getLocalIpResult = await getLocalIpCommand.ExecuteAsync();

            var getPublicIpCommand = new GetPublicIpAddressForThisServerCommand("Get public IP address for this machine");
            var getPublicIpResult = await getPublicIpCommand.ExecuteAsync();

            return Result.Combine(
                setPortResult,
                getLocalIpResult,
                getPublicIpResult)
                    .OnSuccess(() =>
                    {
                        var connectionInfo = new ConnectionInfo
                        {
                            LocalIpAddress = getLocalIpResult.Value,
                            PublicIpAddress = getPublicIpResult.Value,
                            Port = setPortResult.Value
                        };

                        return
                            new TplSocketServer(connectionInfo.LocalIpAddress, connectionInfo.Port)
                            {
                                SocketSettings = _settings.SocketSettings,
                                TransferFolderPath = _settings.TransferFolderPath,
                                TransferUpdateInterval = _settings.TransferUpdateInterval,
                                PublicIpAddress = getPublicIpResult.Value,
                                LoggingEnabled = true
                            };
                    });
        }
    }
}
