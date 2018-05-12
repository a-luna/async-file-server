
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
        readonly RemoteServer _server;
        readonly Logger _log = new Logger(typeof(GetSelectedRemoteServerCommand));

        public GetSelectedRemoteServerCommand(AppState state, RemoteServer server)
        {
            _state = state;
            _server = server;

            ReturnToParent = false;

            ItemText =
                $" Local IP: {_server.ConnectionInfo.LocalIpString}{Environment.NewLine}" +
                $"   Public IP: {_server.ConnectionInfo.PublicIpString}{Environment.NewLine}" +
                $"        Port: {_server.ConnectionInfo.Port}{Environment.NewLine}";
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public async Task<Result> ExecuteAsync()
        {
            _log.Info("Begin: GetSelectedRemoteServerCommand.ExecuteAsync");

            _state.RemoteServerInfo = _server.ConnectionInfo;
            _state.ClientTransferFolderPath = _server.TransferFolder;
            _state.ClientSelected = true;

            _log.Info("Complete: GetSelectedRemoteServerCommand.ExecuteAsync");
            await Task.Delay(1);
            return Result.Ok();
        }
    }
}
