using System.Collections.Generic;
using System.Threading.Tasks;
using AaronLuna.AsyncSocketServer.CLI.Menus.RemoteServerMenus.SelectRemoteServerMenuItems;
using AaronLuna.Common.Console.Menu;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncSocketServer.CLI.Menus.RemoteServerMenus
{
    class SelectRemoteServerMenu : IMenu
    {
        readonly AppState _state;

        public SelectRemoteServerMenu(AppState state)
        {
            _state = state;

            ReturnToParent = false;

            ItemText = _state.RemoteServerSelected
                ? "Select a different server"
                : "Select remote server";

            MenuText = "Choose a remote server:";
            MenuItems = new List<IMenuItem>();
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }
        public string MenuText { get; set; }
        public List<IMenuItem> MenuItems { get; set; }

        public async Task<Result> ExecuteAsync()
        {
            _state.DoNotRefreshMainMenu = true;

            SharedFunctions.DisplayLocalServerInfo(_state);
            PopulateMenu();

            var menuItem = SharedFunctions.GetUserSelection(MenuText, MenuItems, _state);

            var selectRemoteServerResult = await menuItem.ExecuteAsync().ConfigureAwait(false);
            if (selectRemoteServerResult.Success && !(menuItem is ReturnToParentMenuItem))
            {
                _state.RemoteServerSelected = true;
            }

            return selectRemoteServerResult;
        }

        void PopulateMenu()
        {
            MenuItems.Clear();

            foreach (var server in _state.Settings.RemoteServers)
            {
                MenuItems.Add(new GetSelectedRemoteServerMenuItem(_state, server));
            }

            MenuItems.Add(new GetRemoteServerInfoFromUserMenuItem(_state));
            MenuItems.Add(new ReturnToParentMenuItem("Return to main menu"));
        }
    }
}
