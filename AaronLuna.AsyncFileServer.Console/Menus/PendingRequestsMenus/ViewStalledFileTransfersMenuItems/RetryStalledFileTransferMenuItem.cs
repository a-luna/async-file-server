namespace AaronLuna.AsyncFileServer.Console.Menus.PendingRequestsMenus.ViewStalledFileTransfersMenuItems
{
    using System.Threading.Tasks;

    using Common.Console.Menu;
    using Common.Result;

    class RetryStalledFileTransferMenuItem : IMenuItem
    {
        readonly AppState _state;
        readonly int _fileTransferId;

        public RetryStalledFileTransferMenuItem(AppState state, int fileTransferId)
        {
            _state = state;
            _fileTransferId = fileTransferId;

            ReturnToParent = false;
            ItemText = fileTransferId.ToString();
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public async Task<Result> ExecuteAsync()
        {
            var retryFileTransfer = await _state.LocalServer.RetryFileTransferAsync(
                _fileTransferId).ConfigureAwait(false);

            return retryFileTransfer.Success
                ? Result.Ok()
                : retryFileTransfer;
        }
    }
}
