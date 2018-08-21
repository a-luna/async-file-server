using System;
using System.Threading.Tasks;
using AaronLuna.Common.Console.Menu;
using AaronLuna.Common.IO;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncSocketServer.CLI.Menus.RemoteServerMenus.SelectFileMenuItems
{
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
            return
                _state.LocalServer.GetFileAsync(
                    _state.SelectedServerInfo,
                    _fileName,
                    _fileSize,
                    _remoteFolderPath,
                    _state.LocalServer.MyInfo.TransferFolder);
        }
    }
}
