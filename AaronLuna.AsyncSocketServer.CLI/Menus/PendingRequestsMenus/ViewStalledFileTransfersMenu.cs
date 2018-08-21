using System.Collections.Generic;
using System.Threading.Tasks;
using AaronLuna.AsyncSocketServer.CLI.Menus.PendingRequestsMenus.ViewStalledFileTransfersMenuItems;
using AaronLuna.Common.Console.Menu;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncSocketServer.CLI.Menus.PendingRequestsMenus
{
    class ViewStalledFileTransfersMenu : IMenu
    {
        readonly AppState _state;

        public ViewStalledFileTransfersMenu(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = "Retry stalled file transfers";
            MenuText = "Choose a stalled file transfer below to attempt downloading:";
            MenuItems = new List<IMenuItem>();
        }

        public bool ReturnToParent { get; set; }
        public string ItemText { get; set; }
        public string MenuText { get; set; }
        public List<IMenuItem> MenuItems { get; set; }

        public async Task<Result> ExecuteAsync()
        {
            if (_state.StalledFileTransferIds.Count == 0)
            {
                return Result.Fail("There are no stalled file transfers");
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
            foreach (var id in _state.StalledFileTransferIds)
            {
                var fileTransfer = _state.LocalServer.GetFileTransferById(id).Value;

                SharedFunctions.LookupRemoteServerName(
                    fileTransfer.RemoteServerInfo,
                    _state.Settings.RemoteServers);

                MenuItems.Add(new RetryStalledFileTransferMenuItem(_state, fileTransfer.Id));
            }

            MenuItems.Add(new ReturnToParentMenuItem("Return to main menu"));
        }
    }
}
