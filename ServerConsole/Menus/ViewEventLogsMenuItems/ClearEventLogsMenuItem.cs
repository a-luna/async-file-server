namespace ServerConsole.Menus.ViewEventLogsMenuItems
{
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Result;

    class ClearEventLogsMenuItem : IMenuItem
    {
        AppState _state;

        public ClearEventLogsMenuItem(AppState state)
        {
            _state = state;

            ReturnToParent = true;
            ItemText = "Clear list of archived events";
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public Task<Result> ExecuteAsync()
        {
            return Task.Factory.StartNew(Execute);
        }

        Result Execute()
        {
            _state.LocalServer.Archive.Clear();
            return Result.Ok();
        }
    }
}
