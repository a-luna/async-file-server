using System.Collections.Generic;
using System.Threading.Tasks;
using AaronLuna.AsyncSocketServer.CLI.Menus.CommonMenuItems;
using AaronLuna.AsyncSocketServer.CLI.Menus.ServerConfigurationMenus.LocalServerSettingsMenuItems;
using AaronLuna.Common.Console.Menu;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncSocketServer.CLI.Menus.ServerConfigurationMenus
{
    class LocalServerSettingsMenu : IMenu
    {
        readonly AppState _state;

        public LocalServerSettingsMenu(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = Resources.MenuItemText_LocalServerSettings;
            MenuText = Resources.MenuText_LocalServerSettings;
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

                exit = menuItem.ReturnToParent;
                result = await menuItem.ExecuteAsync().ConfigureAwait(false);

                if (menuItem is ReturnToParentMenuItem) continue;

                if (result.Success)
                {
                    var applyChanges = ApplyChanges();
                    if (applyChanges.Failure)
                    {
                        result = Result.Fail(applyChanges.Error);
                        exit = true;
                    }

                    continue;
                }

                exit = true;
            }

            return result;
        }

        void PopulateMenu()
        {
            MenuItems.Clear();
            MenuItems.Add(new GetPortNumberFromUserMenuItem(_state, _state.LocalServer.MyInfo, true));
            MenuItems.Add(new SetLocalServerCidrIpMenuItem(_state));
            MenuItems.Add(new SetSocketBufferSizeMenu(_state));
            MenuItems.Add(new SetSocketListenBacklogSizeMenu(_state));
            MenuItems.Add(new SetSocketTimeoutMenu(_state));
            MenuItems.Add(new SetUpdateIntervalMenu(_state));
            MenuItems.Add(new SetTransferStalledTimeoutMenu(_state));
            MenuItems.Add(new SetRetryLimitMenu(_state));
            MenuItems.Add(new SetRetryLockoutTimeSpanMenu(_state));
            MenuItems.Add(new ReturnToParentMenuItem("Return to main menu"));
        }

        Result ApplyChanges()
        {
            _state.Settings.LocalServerPortNumber = _state.LocalServer.MyInfo.PortNumber;
            _state.Settings.LocalNetworkCidrIp = _state.UserEntryLocalNetworkCidrIp;
            _state.LocalServer.UpdateSettings(_state.Settings);

            return _state.SaveSettingsToFile();
        }
    }
}
