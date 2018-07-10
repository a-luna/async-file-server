namespace AaronLuna.AsyncFileServer.Console.Menus.RemoteServerMenus.SelectRemoteServerMenuItems
{
    using System;
    using System.Threading.Tasks;

    using Common.Console.Menu;
    using Common.Result;

    using Model;

    class GetSelectedRemoteServerMenuItem : IMenuItem
    {
        readonly AppState _state;
        readonly ServerInfo _server;

        public GetSelectedRemoteServerMenuItem(AppState state, ServerInfo server)
        {
            _state = state;
            _server = server;

            ReturnToParent = false;
            ItemText = server.ItemText;
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public Task<Result> ExecuteAsync()
        {
            return Task.Run((Func<Result>) Execute);
        }

        Result Execute()
        {
            _state.SelectedServerInfo = _server;
            _state.RemoteServerSelected = true;

            return Result.Ok();
        }
    }
}
