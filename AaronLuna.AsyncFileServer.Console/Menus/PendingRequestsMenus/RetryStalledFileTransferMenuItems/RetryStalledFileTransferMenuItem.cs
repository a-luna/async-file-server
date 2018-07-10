namespace AaronLuna.AsyncFileServer.Console.Menus.PendingRequestsMenus.RetryStalledFileTransferMenuItems
{
    using System.Threading.Tasks;

    using Common.Console.Menu;
    using Common.Result;

    using Model;

    class RetryStalledFileTransferMenuItem : IMenuItem
    {
        readonly AppState _state;
        readonly FileTransfer _fileTransfer;

        public RetryStalledFileTransferMenuItem(AppState state, FileTransfer fileTransfer)
        {
            _state = state;
            _fileTransfer = fileTransfer;

            ReturnToParent = false;
            ItemText = fileTransfer.ToString();
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public async Task<Result> ExecuteAsync()
        {
            var retryFileTransferesult = await _state.LocalServer.RetryFileTransferAsync(
                _fileTransfer.Id,
                _state.SelectedServerInfo.SessionIpAddress,
                _state.SelectedServerInfo.PortNumber).ConfigureAwait(false);

            return retryFileTransferesult.Success
                ? Result.Ok()
                : retryFileTransferesult;
        }
    }
}
