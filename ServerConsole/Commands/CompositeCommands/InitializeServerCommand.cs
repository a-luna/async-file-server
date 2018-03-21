namespace ServerConsole.Commands.CompositeCommands
{
    using System.Threading.Tasks;
    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Result;
    using Getters;
    using Setters;
    using TplSocketServer;

    class InitializeServerCommand : ICommand<TplSocketServer>
    {
        readonly AppSettings _settings;

        public InitializeServerCommand(AppSettings settings)
        {
            ReturnToParent = false;
            ItemText = "Initialize Local Server";

            _settings = settings;
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public async Task<CommandResult<TplSocketServer>> ExecuteAsync()
        {
            var setPortCommand = new SetPortNumberForLocalServerCommand();
            var setPortResult = await setPortCommand.ExecuteAsync();

            var getLocalIpCommand = new GetLocalIpAddressForThisServerCommand();
            var getLocalIpResult = await getLocalIpCommand.ExecuteAsync();

            var getPublicIpCommand = new GetPublicIpAddressForThisServerCommand();
            var getPublicIpResult = await getPublicIpCommand.ExecuteAsync();

            var result =  Result.Combine(
                        setPortResult.Result,
                        getLocalIpResult.Result,
                        getPublicIpResult.Result)
                            .OnSuccess(() =>
                            {
                                var connectionInfo = new ConnectionInfo
                                {
                                    LocalIpAddress = getLocalIpResult.Result.Value,
                                    PublicIpAddress = getPublicIpResult.Result.Value,
                                    Port = setPortResult.Result.Value
                                };

                                return
                                    new TplSocketServer(connectionInfo.LocalIpAddress, connectionInfo.Port)
                                    {
                                        SocketSettings = _settings.SocketSettings,
                                        TransferFolderPath = _settings.TransferFolderPath,
                                        TransferUpdateInterval = _settings.TransferUpdateInterval,
                                        PublicIpAddress = getPublicIpResult.Result.Value,
                                        LoggingEnabled = true
                                    };
                            });

            return new CommandResult<TplSocketServer>
            {
                ReturnToParent = ReturnToParent,
                Result = result
            };
        }
    }
}
