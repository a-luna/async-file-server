using System;

namespace ServerConsole.Menus.ViewFileTransferEventLogsMenuItems
{
    using System.Linq;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Result;

    class ClearFileTransferEventLogsMenuItem : IMenuItem
    {
        readonly AppState _state;

        public ClearFileTransferEventLogsMenuItem(AppState state)
        {
            _state = state;

            ReturnToParent = true;
            ItemText = "Clear file transfer list";
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public Task<Result> ExecuteAsync()
        {
            return Task.Run((Func<Result>) Execute);
        }

        Result Execute()
        {
            _state.LogViewerFileTransferId = _state.LocalServer.NewestTransferId;

            return Result.Ok();
        }
    }
}
