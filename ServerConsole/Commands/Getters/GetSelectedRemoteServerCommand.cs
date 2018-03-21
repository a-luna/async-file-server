namespace ServerConsole.Commands.Getters
{
    using System;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Result;
    using TplSocketServer;

    class GetSelectedRemoteServerCommand : ICommand<RemoteServer>
    {
        readonly RemoteServer _server;

        public GetSelectedRemoteServerCommand(RemoteServer server)
        {
            _server = server;

            ReturnToParent = false;

            ItemText =
                $"  Local IP: {_server.ConnectionInfo.LocalIpString}{Environment.NewLine}" +
                $"   Public IP: {_server.ConnectionInfo.PublicIpString}{Environment.NewLine}" +
                $"        Port: {_server.ConnectionInfo.Port}{Environment.NewLine}";
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public async Task<CommandResult<RemoteServer>> ExecuteAsync()
        {
            await Task.Delay(1);
            return new CommandResult<RemoteServer>
            {
                ReturnToParent = ReturnToParent,
                Result = Result.Ok(_server)
            };
        }
    }
}
