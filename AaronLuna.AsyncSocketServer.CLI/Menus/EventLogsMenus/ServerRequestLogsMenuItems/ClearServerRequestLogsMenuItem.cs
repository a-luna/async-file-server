using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AaronLuna.Common.Console.Menu;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncSocketServer.CLI.Menus.EventLogsMenus.ServerRequestLogsMenuItems
{
    class ClearServerRequestLogsMenuItem : IMenuItem
    {
        readonly AppState _state;
        readonly List<int> _requestIds;

        public ClearServerRequestLogsMenuItem(AppState state)
        {
            _state = state;
            _requestIds = _state.RequestIds;

            ReturnToParent = true;
            ItemText = $"Clear server request list{Environment.NewLine}";
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public Task<Result> ExecuteAsync()
        {
            return Task.Run((Func<Result>)Execute);
        }

        Result Execute()
        {
            if (_requestIds.Count == 0) return Result.Ok();

            _state.LogViewerRequestBoundary = _state.MostRecentRequestTime;

            return Result.Ok();
        }
    }
}
