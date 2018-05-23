﻿namespace ServerConsole.Commands.ServerCommands
{
    using System.IO;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.IO;
    using AaronLuna.Common.Result;

    class SendFileMenuItem : IMenuItem
    {
        readonly AppState _state;
        readonly string _outgoingFilePath;

        public SendFileMenuItem(AppState state, string outgoingFilePath)
        {
            _state = state;
            _outgoingFilePath = outgoingFilePath;

            ReturnToParent = false;

            var fileName = Path.GetFileName(outgoingFilePath);
            var fileSize = new FileInfo(outgoingFilePath).Length;
            ItemText = $"{fileName} ({FileHelper.FileSizeToString(fileSize)})";
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public async Task<Result> ExecuteAsync()
        {
            var ipAddress = _state.SelectedServer.SessionIpAddress;
            var port = _state.SelectedServer.Port;
            var transferFolderPath = _state.SelectedServer.TransferFolder;

            var sendFileResult =
                await _state.LocalServer.SendFileAsync(
                    ipAddress,
                    port,
                    _outgoingFilePath,
                    transferFolderPath);

            return sendFileResult.Success
                ? Result.Ok()
                : sendFileResult;
        }
    }
}
