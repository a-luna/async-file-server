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
            ReturnToParent = true;
            ItemText = "Shutdown";
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }
        public async Task<Result> ExecuteAsync()
        {
            await Task.Delay(1);
            return Result.Ok(true);
        }
    }
}
