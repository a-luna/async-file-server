
namespace ServerConsole.Commands.Getters
{
    using System;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Logging;
    using AaronLuna.Common.Result;
    using TplSockets;

    class GetSelectedRemoteServerCommand : ICommand
    {
        readonly AppState _state;
        readonly ServerInfo _server;
        readonly Logger _log = new Logger(typeof(GetSelectedRemoteServerCommand));

        public GetSelectedRemoteServerCommand(AppState state, ServerInfo server)
        {
            _state = state;
            _server = server;

            ReturnToParent = false;

            ItemText =
                $" Local IP: {_server.LocalIpString}{Environment.NewLine}" +
                $"   Public IP: {_server.PublicIpString}{Environment.NewLine}" +
                $"        Port: {_server.Port}{Environment.NewLine}";
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public async Task<Result> ExecuteAsync()
        {
            _state.RemoteServerInfo = _server;
            _state.ClientSelected = true;
            
            await Task.Delay(1);
            return Result.Ok();
        }
    }
}
