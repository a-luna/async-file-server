namespace ServerConsole.Commands.ServerCommands
{
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Logging;
    using AaronLuna.Common.Result;

    class ShutdownServerCommand : ICommand
    {
        readonly Logger _log = new Logger(typeof(ShutdownServerCommand));

        public ShutdownServerCommand()
        {
            _log.Info("Begin: Instantiate ShutdownServerCommand");

            ReturnToParent = true;
            ItemText = "Shutdown";

            _log.Info("Complete: Instantiate ShutdownServerCommand");
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }
        public async Task<Result> ExecuteAsync()
        {
            _log.Info("Begin: ShutdownServerCommand.ExecuteAsync");

            await Task.Delay(1);

            _log.Info("Complete: ShutdownServerCommand.ExecuteAsync");
            
            return Result.Ok(true);

        }
    }
}
