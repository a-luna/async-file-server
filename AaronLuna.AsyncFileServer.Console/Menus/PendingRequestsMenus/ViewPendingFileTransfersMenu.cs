namespace AaronLuna.AsyncFileServer.Console.Menus.PendingRequestsMenus
{
    using System.Collections.Generic;
    using System.Threading.Tasks;

    using Common.Console.Menu;
    using Common.Result;

    using ViewPendingFileTransfersMenuItems;

    class ViewPendingFileTransfersMenu : IMenu
    {
        readonly AppState _state;

        public ViewPendingFileTransfersMenu(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = "View pending file transfers";
            MenuText = "Select a pending file transfer to view details of the incoming request:";
            MenuItems = new List<IMenuItem>();
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }
        public string MenuText { get; set; }
        public List<IMenuItem> MenuItems { get; set; }
        
        public async Task<Result> ExecuteAsync()
        {
            if (_state.NoFileTransfersPending)
            {
                return Result.Fail("There are no pending file transfers");
            }

            _state.DoNotRefreshMainMenu = true;
            SharedFunctions.DisplayLocalServerInfo(_state);
            PopulateMenu();

            var menuItem = SharedFunctions.GetUserSelection(MenuText, MenuItems, _state);
            return await menuItem.ExecuteAsync().ConfigureAwait(false);
        }

        void PopulateMenu()
        {
            MenuItems.Clear();
            foreach (var id in _state.PendingFileTransferIds)
            {
                var fileTransfer = _state.LocalServer.GetFileTransferById(id).Value;

                SharedFunctions.LookupRemoteServerName(
                    fileTransfer.RemoteServerInfo,
                    _state.Settings.RemoteServers);

                MenuItems.Add(new ProcessInboundFileTransferMenuItem(_state, fileTransfer));
            }

            MenuItems.Add(new ReturnToParentMenuItem("Return to main menu"));
        }
    }
}
