namespace AaronLuna.AsyncFileServer.Console.Menus.RemoteServerMenus
{
    using System.Collections.Generic;
    using System.Threading.Tasks;

    using Common.Console.Menu;
    using Common.Result;

    using Model;
    using CommonMenuItems;
    using EditServerInfoMenuItems;

    class EditServerInfoMenu : IMenu
    {
        readonly AppState _state;

        public EditServerInfoMenu(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = "Edit server info";
            MenuText = "Select the value you wish to edit from the list below:";
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

                var menuItem =
                    await SharedFunctions.GetUserSelectionAsync(MenuText, MenuItems, _state).ConfigureAwait(false);

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
            MenuItems.Add(new GetIpAddressFromUserMenuItem(_state));
            MenuItems.Add(new GetPortNumberFromUserMenuItem(_state, _state.SelectedServerInfo, false));
            MenuItems.Add(new GetServerNameFromUserMenuItem(_state));
            MenuItems.Add(new ReturnToParentMenuItem("Return to main menu"));
        }

        Result ApplyChanges()
        {
            var serverFromFile =
                SharedFunctions.GetRemoteServer(_state.SelectedServerInfo, _state.Settings.RemoteServers).Value;

            serverFromFile.SessionIpAddress = _state.SelectedServerInfo.SessionIpAddress;
            serverFromFile.PortNumber = _state.SelectedServerInfo.PortNumber;
            serverFromFile.Name = _state.SelectedServerInfo.Name;

            return ServerSettings.SaveToFile(_state.Settings, _state.SettingsFilePath);
        }
    }
}
