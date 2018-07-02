namespace AaronLuna.AsyncFileServer.Console.Menus.MainMenuItems
{
    using System.Threading.Tasks;

    using Common.Console.Menu;
    using Common.Result;

    class ShutdownServerMenuItem : IMenuItem
    {
        readonly AppState _state;

        public ShutdownServerMenuItem(AppState state)
        {
            _state = state;
            ItemText = "Shutdown local server";
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public async Task<Result> ExecuteAsync()
        {
            ReturnToParent = false;
            var shutdown = SharedFunctions.PromptUserYesOrNo("Shutdown server?");
            if (!shutdown) return Result.Ok();

            await _state.LocalServer.ShutdownAsync();
            ReturnToParent = true;

            return Result.Ok();
        }
    }
}
