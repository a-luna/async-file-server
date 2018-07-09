namespace AaronLuna.AsyncFileServer.Console.Menus.SelectFileMenuItems
{
    using System.IO;
    using System.Threading.Tasks;

    using Common.Console.Menu;
    using Common.IO;
    using Common.Result;

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
            var ipAddress = _state.SelectedServerInfo.SessionIpAddress;
            var port = _state.SelectedServerInfo.PortNumber;
            var serverName = _state.SelectedServerInfo.Name;
            var transferFolderPath = _state.SelectedServerInfo.TransferFolder;
            
            var sendFileResult =
                await _state.LocalServer.SendFileAsync(
                    ipAddress,
                    port,
                    serverName,
                    _outgoingFilePath,
                    transferFolderPath);

            return sendFileResult.Success
                ? Result.Ok()
                : sendFileResult;
        }
    }
}
