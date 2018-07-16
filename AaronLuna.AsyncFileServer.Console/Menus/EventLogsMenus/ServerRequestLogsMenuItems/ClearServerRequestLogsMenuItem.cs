namespace AaronLuna.AsyncFileServer.Console.Menus.EventLogsMenus.ServerRequestLogsMenuItems
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;

    using Common.Console.Menu;
    using Common.Result;

    class ClearServerRequestLogsMenuItem : IMenuItem
    {
        readonly AppState _state;
        readonly List<int> _requestIds;

        public ClearServerRequestLogsMenuItem(AppState state)
        {
            _state = state;
            _requestIds = _state.LocalServer.RequestIds;

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

            _state.LogViewerRequestId = _requestIds.Last();

            return Result.Ok();
        }
    }
}
