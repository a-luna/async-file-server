using System.Collections.Generic;
using System.Threading.Tasks;
using AaronLuna.AsyncSocketServer.CLI.Menus.ServerConfigurationMenus.LocalServerNetworkPropertiesMenuItems;
using AaronLuna.Common.Console.Menu;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncSocketServer.CLI.Menus.ServerConfigurationMenus
{
    class LocalServerNetworkPropertiesMenu : IMenu
    {
        AppState _state;

        public LocalServerNetworkPropertiesMenu(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = "Network interface info/stats";
            MenuText = Resources.Menu_NetworkProperties;
            MenuItems = new List<IMenuItem>();
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }
        public string MenuText { get; set; }
        public List<IMenuItem> MenuItems { get; set; }

        public async Task<Result> ExecuteAsync()
        {
            _state.DoNotRefreshMainMenu = true;
            var exit = false;
            Result result = null;

            while (!exit)
            {
                SharedFunctions.DisplayLocalServerInfo(_state);
                PopulateMenu();

                var menuItem = SharedFunctions.GetUserSelection(MenuText, MenuItems, _state);
                result = await menuItem.ExecuteAsync().ConfigureAwait(false);
                exit = menuItem.ReturnToParent;

                if (result.Success) continue;

                exit = true;
            }

            return result;
        }

        void PopulateMenu()
        {
            MenuItems.Clear();
            MenuItems.Add(new DisplayLocalIPv4AddressesMenuItem(_state));
            MenuItems.Add(new DisplayIPv4GlobalStatsMenuItem(_state));
            MenuItems.Add(new DisplayTcpIPv4StatsMenuItem(_state));
            MenuItems.Add(new DisplayIcmpV4StatsMenuItem(_state));
            MenuItems.Add(new ReturnToParentMenuItem("Return to main menu"));
        }
    }
}
