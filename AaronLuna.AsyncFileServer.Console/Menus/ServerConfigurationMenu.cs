namespace AaronLuna.AsyncFileServer.Console.Menus
{
    using System.Collections.Generic;
    using System.Threading.Tasks;

    using ServerConfigurationMenuItems;
    using ServerConfigurationMenus;
    using Common.Console.Menu;
    using Common.Result;

    class ServerConfigurationMenu : IMenu
    {
        readonly AppState _state;

        public ServerConfigurationMenu(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = "Server configuration";
            MenuText = Resources.Menu_ChangeSettings;

            MenuItems = new List<IMenuItem>
            {
                new SetMyPortNumberMenuItem(state),
                new SetMyCidrIpMenuItem(state),
                new DisplayLocalIPv4AddressesMenuItem(),

                new SetSocketBufferSizeMenu(state),
                new SetSocketListenBacklogSizeMenu(state),
                new SetSocketTimeoutMenu(state),

                new SetTransferUpdateIntervalMenuItem(state),
                new SetTransferStalledTimeoutMenuItem(state),
                new SetTransferRetryLimitMenuItem(state),
                new SetTransferRetryLockoutTimeSpan(state),

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
                _state.DisplayCurrentStatus();

                var menuItem = await SharedFunctions.GetUserSelectionAsync(MenuText, MenuItems, _state);
                result = await menuItem.ExecuteAsync();

                if (result.Success && !(menuItem is ReturnToParentMenuItem))
                {
                    ApplyChanges();
                    exit = true;
                    continue;
                }

                exit = menuItem.ReturnToParent;
                if (result.Success) continue;
                
                exit = true;
            }
            return result;
        }

        void ApplyChanges()
        {
            _state.Settings.LocalServerPortNumber = _state.UserEntryLocalServerPort;
            _state.Settings.LocalNetworkCidrIp = _state.UserEntryLocalNetworkCidrIp;

            ServerSettings.SaveToFile(_state.Settings, _state.SettingsFilePath);
        }
    }
}
