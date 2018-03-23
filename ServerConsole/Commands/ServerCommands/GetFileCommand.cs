namespace ServerConsole.Commands.ServerCommands
{
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Result;

    class GetFileCommand : ICommand
    {
        public GetFileCommand()
        {
            ReturnToParent = false;
            ItemText = "Get file";
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
