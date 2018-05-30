namespace ServerConsole.Menus.MainMenuItems
{
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Result;

    class RetryStalledFileTransferMenuItem : IMenuItem
    {
        readonly AppState _state;

        public RetryStalledFileTransferMenuItem(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = "Retry stalled file transfer";
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public async Task<Result> ExecuteAsync()
        {
            _state.RetryCounter++;
            return await _state.LocalServer.RetryLastFileTransferAsync(
                _state.SelectedServer.SessionIpAddress,
                _state.SelectedServer.Port);
        }
    }
}
