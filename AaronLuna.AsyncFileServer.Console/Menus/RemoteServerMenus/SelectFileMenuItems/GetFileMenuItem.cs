namespace AaronLuna.AsyncFileServer.Console.Menus.RemoteServerMenus.SelectFileMenuItems
{
    using System;
    using System.Threading.Tasks;

    using Common.Console.Menu;
    using Common.IO;
    using Common.Result;

    class GetFileMenuItem : IMenuItem
    {
        readonly AppState _state;
        readonly string _fileName;
        readonly string _remoteFolderPath;
        readonly long _fileSize;

        public GetFileMenuItem(
            AppState state,
            string fileName,
            string remoteFolderPath,
            long fileSize,
            bool isLastMenuItem)
        {
            _state = state;
            _fileName = fileName;
            _remoteFolderPath = remoteFolderPath;
            _fileSize = fileSize;

            ReturnToParent = false;
            
            var menuItem = $"{fileName} ({FileHelper.FileSizeToString(fileSize)})";

            ItemText = isLastMenuItem
                ? menuItem + Environment.NewLine
                : menuItem;
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
                    _fileName,
                    _fileSize,
                    _remoteFolderPath,
                    _state.LocalServer.MyInfo.TransferFolder);
        }
    }
}
