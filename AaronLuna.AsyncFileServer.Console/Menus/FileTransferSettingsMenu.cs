namespace AaronLuna.AsyncFileServer.Console.Menus
{
    using System.Collections.Generic;
    using System.Threading.Tasks;

    using FileTransferSettingsMenuItems;
    using Common.Console.Menu;
    using Common.Result;

    class FileTransferSettingsMenu : IMenu
    {
        readonly AppState _state;

        public FileTransferSettingsMenu(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = "File transfer settings";
            MenuText = Resources.Menu_ChangeSettings;
            MenuItems = new List<IMenuItem>
            {
                new SetTransferUpdateIntervalMenu(_state),
                new SetTransferStalledTimeoutMenu(_state),
                new SetTransferRetryLimitMenu(_state),
                new SetTransferRetryLockoutTimeSpanMenu(_state),
                new SetFileTransferEventLogLevelMenu(_state),
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
    }
}
