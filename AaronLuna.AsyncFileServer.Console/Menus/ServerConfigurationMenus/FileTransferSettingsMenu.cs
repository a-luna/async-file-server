namespace AaronLuna.AsyncFileServer.Console.Menus.ServerConfigurationMenus
{
    using System.Collections.Generic;
    using System.Threading.Tasks;

    using Common.Console.Menu;
    using Common.Result;

    using Model;
    using FileTransferSettingsMenuItems;

    class FileTransferSettingsMenu : IMenu
    {
        readonly AppState _state;

        public FileTransferSettingsMenu(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = "File transfer settings";
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

                var menuItem = await SharedFunctions.GetUserSelectionAsync(MenuText, MenuItems, _state).ConfigureAwait(false);
                result = await menuItem.ExecuteAsync().ConfigureAwait(false);

                if (result.Success && !(menuItem is ReturnToParentMenuItem))
                {
                    var applyChanges =
                        ServerSettings.SaveToFile(_state.Settings, _state.SettingsFilePath);

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

        void PopulateMenu()
        {
            MenuItems.Clear();
            MenuItems.Add(new SetUpdateIntervalMenu(_state));
            MenuItems.Add(new SetTransferStalledTimeoutMenu(_state));
            MenuItems.Add(new SetRetryLimitMenu(_state));
            MenuItems.Add(new SetRetryLockoutTimeSpanMenu(_state));
            MenuItems.Add(new ReturnToParentMenuItem("Return to main menu"));
        }
    }
}
