namespace AaronLuna.AsyncFileServer.Console.Menus.PendingRequestsMenus.RetryStalledFileTransferMenuItems
{
    using System.Threading.Tasks;

    using Common.Console.Menu;
    using Common.Result;

    using Model;

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
            var retryFileTransferesult = await _state.LocalServer.RetryFileTransferAsync(
                _fileTransferId,
                _state.SelectedServerInfo.SessionIpAddress,
                _state.SelectedServerInfo.PortNumber).ConfigureAwait(false);

            return retryFileTransferesult.Success
                ? Result.Ok()
                : retryFileTransferesult;
        }
    }
}
