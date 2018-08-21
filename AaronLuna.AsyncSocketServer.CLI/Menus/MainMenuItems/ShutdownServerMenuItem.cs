using System.Threading.Tasks;
using AaronLuna.Common.Console.Menu;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncSocketServer.CLI.Menus.MainMenuItems
{
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
            SharedFunctions.DisplayLocalServerInfo(_state);

            ReturnToParent = false;
            var shutdown = SharedFunctions.PromptUserYesOrNo(_state, "Shutdown server?");
            if (!shutdown) return Result.Ok();

            await _state.LocalServer.ShutdownAsync().ConfigureAwait(false);
            ReturnToParent = true;

            return Result.Ok();
        }
    }
}
