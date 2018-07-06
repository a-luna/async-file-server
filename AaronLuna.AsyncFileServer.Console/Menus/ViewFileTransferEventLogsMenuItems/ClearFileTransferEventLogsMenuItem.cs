using System.Linq;

namespace AaronLuna.AsyncFileServer.Console.Menus.ViewFileTransferEventLogsMenuItems
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    using Common.Console.Menu;
    using Common.Result;

    class ClearFileTransferEventLogsMenuItem : IMenuItem
    {
        readonly AppState _state;
        readonly List<int> _fileTransferIds;

        public ClearFileTransferEventLogsMenuItem(AppState state)
        {
            _state = state;
            _fileTransferIds = _state.LocalServer.FileTransferIds;

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
            if (_fileTransferIds.Count == 0) return Result.Ok();

            _state.LogViewerFileTransferId = _fileTransferIds.Last();

            return Result.Ok();
        }
    }
}
