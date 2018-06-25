namespace AaronLuna.AsyncFileServer.Console.Menus.SelectRemoteServerMenuItems
{
    using System;
    using System.Threading.Tasks;

    using Model;
    using Common.Console.Menu;
    using Common.Result;

    class GetSelectedRemoteServerMenuItem : IMenuItem
    {
        readonly AppState _state;
        readonly ServerInfo _server;

        public GetSelectedRemoteServerMenuItem(AppState state, ServerInfo server)
        {
            _state = state;
            _server = server;

            ReturnToParent = false;

            ItemText =
                $"IP: {_server.SessionIpAddress}{Environment.NewLine}" +
                $"   Port: {_server.PortNumber}{Environment.NewLine}";
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public Task<Result> ExecuteAsync()
        {
            return Task.Run((Func<Result>) Execute);
        }

        Result Execute()
        {
            _state.SelectedServer = _server;
            _state.ClientSelected = true;
            
            return Result.Ok();
        }
    }
}
