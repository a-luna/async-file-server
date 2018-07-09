namespace AaronLuna.AsyncFileServer.Console.Menus
{
    using System.Collections.Generic;
    using System.Threading.Tasks;

    using CommonMenuItems;
    using LocalServerSettingsMenuItems;
    using Common.Console.Menu;
    using Common.Result;

    class LocalServerSettingsMenu : IMenu
    {
        readonly AppState _state;

        public LocalServerSettingsMenu(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = "Local server settings";
            MenuText = Resources.Menu_ChangeSettings;
            MenuItems = new List<IMenuItem>
            {
                new GetPortNumberFromUserMenuItem(_state, _state.LocalServer.Info, true),
                new SetLocalServerCidrIpMenuItem(_state),
                new DisplayLocalIPv4AddressesMenuItem(),
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
                result = await menuItem.ExecuteAsync().ConfigureAwait(false);

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
            _state.Settings.LocalServerPortNumber = _state.LocalServer.Info.PortNumber;
            _state.Settings.LocalNetworkCidrIp = _state.UserEntryLocalNetworkCidrIp;

            return ServerSettings.SaveToFile(_state.Settings, _state.SettingsFilePath);
        }
    }
}
