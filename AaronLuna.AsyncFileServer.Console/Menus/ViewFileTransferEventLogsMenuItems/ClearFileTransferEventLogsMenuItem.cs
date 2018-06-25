namespace AaronLuna.AsyncFileServer.Console.Menus.ViewFileTransferEventLogsMenuItems
{
    using System;
    using System.Threading.Tasks;

    using Common.Console.Menu;
    using Common.Result;

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
