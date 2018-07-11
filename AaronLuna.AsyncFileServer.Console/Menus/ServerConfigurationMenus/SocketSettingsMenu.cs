namespace AaronLuna.AsyncFileServer.Console.Menus.ServerConfigurationMenus
{
    using System.Collections.Generic;
    using System.Threading.Tasks;

    using Common.Console.Menu;
    using Common.Result;

    using SocketSettingsMenuItems;

    class SocketSettingsMenu : IMenu
    {
        readonly AppState _state;

        public SocketSettingsMenu(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = "Socket settings";
            MenuText = Resources.Menu_ChangeSettings;
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
                    var applyChanges =
                        ServerSettings.SaveToFile(_state.Settings, _state.SettingsFilePath);

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
            MenuItems.Add(new SetSocketBufferSizeMenu(_state));
            MenuItems.Add(new SetSocketListenBacklogSizeMenu(_state));
            MenuItems.Add(new SetSocketTimeoutMenu(_state));
            MenuItems.Add(new ReturnToParentMenuItem("Return to main menu"));
        }
    }
}
