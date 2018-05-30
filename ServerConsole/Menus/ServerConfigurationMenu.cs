namespace ServerConsole.Menus
{
    using System.Collections.Generic;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Result;

    using ServerConfigurationMenuItems;

    using TplSockets;

    class ServerConfigurationMenu : IMenu
    {
        readonly AppState _state;

        public ServerConfigurationMenu(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = "Server configuration";
            MenuText = Resources.Menu_ChangeSettings;
            MenuItems = new List<IMenuItem>();

            MenuItems.Add(new SetMyPortNumberMenuItem(state));
            MenuItems.Add(new SetMyCidrIpMenuItem(state));
            MenuItems.Add(new ReturnToParentMenuItem("Return to main menu"));
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }
        public string MenuText { get; set; }
        public List<IMenuItem> MenuItems { get; set; }

        public Task<Result> DisplayMenuAsync()
        {
            return Task.Run(() => DisplayMenu());
        }

        public Result DisplayMenu()
        {
            _state.DisplayCurrentStatus();
            Menu.DisplayMenu(MenuText, MenuItems);

            return Result.Ok();
        }

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
            _state.Settings.LocalPort = _state.UserEntryLocalServerPort;
            _state.Settings.LocalNetworkCidrIp = _state.UserEntryLocalNetworkCidrIp;

            ServerSettings.SaveToFile(_state.Settings, _state.SettingsFilePath);
        }
    }
}
