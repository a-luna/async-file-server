using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AaronLuna.Common.Console.Menu;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncSocketServer.CLI.Menus.EventLogsMenus.FileTransferLogsMenuItems
{
    class ClearFileTransferEventLogsMenuItem : IMenuItem
    {
        readonly AppState _state;
        readonly List<int> _fileTransferIds;

        public ClearFileTransferEventLogsMenuItem(AppState state)
        {
            _state = state;
            _fileTransferIds = _state.FileTransferIds;

            ReturnToParent = true;
            ItemText = $"Clear file transfer list{Environment.NewLine}";
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
