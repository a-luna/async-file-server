namespace ServerConsole.Commands.ServerCommands
{
    using System.IO;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.IO;
    using AaronLuna.Common.Result;

    class GetFileMenuItem : IMenuItem
    {
        readonly AppState _state;
        readonly string _remoteFilePath;

        public GetFileMenuItem(AppState state, string remoteFilePath, long fileSize)
        {
            _state = state;
            _remoteFilePath = remoteFilePath;

            ReturnToParent = false;

            var fileName = Path.GetFileName(remoteFilePath);
            ItemText = $"{fileName} ({FileHelper.FileSizeToString(fileSize)})";
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public async Task<Result> ExecuteAsync()
        {
            var remoteIp = _state.RemoteServerInfo.SessionIpString;
            var remotePort = _state.RemoteServerInfo.Port;

            var getFileResult =
                await _state.LocalServer.GetFileAsync(
                    remoteIp,
                    remotePort,
                    _remoteFilePath,
                    _state.LocalServer.MyTransferFolderPath).ConfigureAwait(false);

            return getFileResult.Success
                ? Result.Ok()
                : getFileResult;
        }
    }
}
