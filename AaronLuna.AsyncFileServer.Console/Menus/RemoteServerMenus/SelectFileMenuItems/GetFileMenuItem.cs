namespace AaronLuna.AsyncFileServer.Console.Menus.RemoteServerMenus.SelectFileMenuItems
{
    using System.IO;
    using System.Threading.Tasks;

    using Common.Console.Menu;
    using Common.IO;
    using Common.Result;

    class GetFileMenuItem : IMenuItem
    {
        readonly AppState _state;
        readonly string _remoteFilePath;
        readonly long _fileSize;

        public GetFileMenuItem(AppState state, string remoteFilePath, long fileSize)
        {
            _state = state;
            _remoteFilePath = remoteFilePath;
            _fileSize = fileSize;

            ReturnToParent = false;

            var fileName = Path.GetFileName(remoteFilePath);
            ItemText = $"{fileName} ({FileHelper.FileSizeToString(fileSize)})";
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public Task<Result> ExecuteAsync()
        {
            var remoteIp = _state.SelectedServerInfo.SessionIpAddress;
            var remotePort = _state.SelectedServerInfo.PortNumber;
            var serverName = _state.SelectedServerInfo.Name;

            return
                _state.LocalServer.GetFileAsync(
                    remoteIp,
                    remotePort,
                    serverName,
                    _remoteFilePath,
                    _fileSize,
                    _state.LocalServer.MyTransferFolderPath);
        }
    }
}
