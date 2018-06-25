using System.Threading.Tasks;
using AaronLuna.AsyncFileServer.Model;
using AaronLuna.Common.Console.Menu;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncFileServer.Console.Menus.RetryStalledFileTransferMenuItems
{
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
                _state.SelectedServer.SessionIpAddress,
                _state.SelectedServer.PortNumber);

            return retryFileTransferesult.Success
                ? Result.Ok()
                : retryFileTransferesult;
        }
    }
}
