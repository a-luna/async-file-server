using System;
using System.IO;
using System.Threading.Tasks;
using AaronLuna.Common.Console.Menu;
using AaronLuna.Common.IO;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncSocketServer.CLI.Menus.RemoteServerMenus.SelectFileMenuItems
{
    class SendFileMenuItem : IMenuItem
    {
        readonly AppState _state;
        readonly string _fileName;
        readonly long _fileSizeInBytes;
        readonly string _localFolderPath;

        public SendFileMenuItem(AppState state, string outgoingFilePath, bool isLastMenuItem)
        {
            _state = state;

            ReturnToParent = false;

            _fileName = Path.GetFileName(outgoingFilePath);
            _localFolderPath = Path.GetDirectoryName(outgoingFilePath);
            _fileSizeInBytes = new FileInfo(outgoingFilePath).Length;

            var menuItem = $"{_fileName} ({FileHelper.FileSizeToString(_fileSizeInBytes)})";

            ItemText = isLastMenuItem
                ? menuItem + Environment.NewLine
                : menuItem;
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public async Task<Result> ExecuteAsync()
        {
            var transferFolderPath = _state.SelectedServerInfo.TransferFolder;

            var sendFileResult =
                await _state.LocalServer.SendFileAsync(
                    _state.SelectedServerInfo,
                    _fileName,
                    _fileSizeInBytes,
                    _localFolderPath,
                    transferFolderPath).ConfigureAwait(false);

            return sendFileResult.Success
                ? Result.Ok()
                : sendFileResult;
        }
    }
}
