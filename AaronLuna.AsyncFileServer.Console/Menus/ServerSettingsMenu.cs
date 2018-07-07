namespace AaronLuna.AsyncFileServer.Console.Menus
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    using ServerConfigurationMenuItems;
    using ServerConfigurationMenus;
    using Common.Console.Menu;
    using Common.Result;

    class ServerSettingsMenu : IMenu
    {
        readonly AppState _state;
        readonly TieredMenu _tieredMenu;

        public ServerSettingsMenu(AppState state)
        {
            _state = state;
            _tieredMenu = new TieredMenu();

            ReturnToParent = false;
            ItemText = "Server settings";
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

                var menuItem = await GetUserSelectionAsync();
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

        void PopulateMenu()
        {
            _tieredMenu.Clear();

            var localServerSettingsMenuTier = new MenuTier
            {
                TierLabel = "Local Server Settings:",
                MenuItems = new List<IMenuItem>
                {
                    new GetPortNumberFromUserMenuItem(_state, _state.LocalServer.Info, true),
                    new SetLocalServerCidrIpMenuItem(_state),
                    new DisplayLocalIPv4AddressesMenuItem(),
                }
            };

            var socketSettingsMenuTier = new MenuTier
            {
                TierLabel = "Socket Settings",
                MenuItems = new List<IMenuItem>
                {
                    new SetSocketBufferSizeMenu(_state),
                    new SetSocketListenBacklogSizeMenu(_state),
                    new SetSocketTimeoutMenu(_state),
                }
            };

            var filetransferSettingsMenuTier = new MenuTier
            {
                TierLabel = "File Transfer Settings:",
                MenuItems = new List<IMenuItem>
                {
                    new SetTransferUpdateIntervalMenu(_state),
                    new SetTransferStalledTimeoutMenu(_state),
                    new SetTransferRetryLimitMenu(_state),
                    new SetTransferRetryLockoutTimeSpanMenu(_state),
                    new SetFileTransferEventLogLevelMenu(_state)
                }
            };

            var returnToMainMenuTier = new MenuTier
            {
                MenuItems = new List<IMenuItem>
                {
                    new ReturnToParentMenuItem("Return to main menu")
                }
            };

            _tieredMenu.Add(localServerSettingsMenuTier);
            _tieredMenu.Add(socketSettingsMenuTier);
            _tieredMenu.Add(filetransferSettingsMenuTier);
            _tieredMenu.Add(returnToMainMenuTier);
        }

        async Task<IMenuItem> GetUserSelectionAsync()
        {
            var userSelection = 0;
            while (userSelection == 0)
            {
                Menu.DisplayTieredMenu(_tieredMenu);
                Console.WriteLine($"Enter a menu item number (valid range 1-{_tieredMenu.Count}):");
                var input = Console.ReadLine();

                var validationResult = SharedFunctions.ValidateNumberIsWithinRange(input, 1, _tieredMenu.Count);
                if (validationResult.Failure)
                {
                    Console.WriteLine(Environment.NewLine + validationResult.Error);
                    await Task.Delay(_state.MessageDisplayTime);

                    SharedFunctions.DisplayLocalServerInfo(_state);
                    continue;
                }

                userSelection = validationResult.Value;
            }

            return _tieredMenu.GetMenuItem(userSelection - 1);
        }

        Result ApplyChanges()
        {
            _state.Settings.LocalServerPortNumber = _state.LocalServer.Info.PortNumber;
            _state.Settings.LocalNetworkCidrIp = _state.UserEntryLocalNetworkCidrIp;

            var saveSettings = ServerSettings.SaveToFile(_state.Settings, _state.SettingsFilePath);
            if (saveSettings.Failure)
            {
                return saveSettings;
            }

            return Result.Ok();
        }
    }
}
