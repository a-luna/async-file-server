namespace AaronLuna.AsyncFileServer.Console.Menus
{
    using System.Collections.Generic;
    using System.Threading.Tasks;

    using Common.Console.Menu;
    using Common.Result;
    using ServerConfigurationMenuItems;

    class UpdateSelectedServerInfoMenu : IMenu
    {
        readonly AppState _state;
        public UpdateSelectedServerInfoMenu(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = "Edit server info";
            MenuText = "Select the value you wish to edit from the list below:";
            MenuItems = new List<IMenuItem>
            {
                new GetIpAddressFromUserMenuItem(state),
                new GetPortNumberFromUserMenuItem(state, state.SelectedServerInfo, false),
                new GetServerNameFromUserMenuItem(state),
                new ReturnToParentMenuItem("Return to main menu")
            };
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
                var menuItem = await SharedFunctions.GetUserSelectionAsync(MenuText, MenuItems, _state);                
                result = await menuItem.ExecuteAsync();

                if (result.Success && !(menuItem is ReturnToParentMenuItem))
                {
                    var applyChanges = ApplyChanges();
                    if (applyChanges.Failure)
                    {
                        result = Result.Fail(applyChanges.Error);
                    }

                    exit = true;
                    continue;
                }
                exit = menuItem.ReturnToParent;
                if (result.Success) continue;

                exit = true;
            }

            return result;
        }

        Result ApplyChanges()
        {
            var serverFromFile =
                SharedFunctions.GetRemoteServer(_state.SelectedServerInfo, _state.Settings.RemoteServers);

            serverFromFile.SessionIpAddress = _state.SelectedServerInfo.SessionIpAddress;
            serverFromFile.PortNumber = _state.SelectedServerInfo.PortNumber;
            serverFromFile.Name = _state.SelectedServerInfo.Name;

            var saveSettings = ServerSettings.SaveToFile(_state.Settings, _state.SettingsFilePath);

            return saveSettings.Success
                ? Result.Ok()
                : saveSettings;
        }

    }
}
