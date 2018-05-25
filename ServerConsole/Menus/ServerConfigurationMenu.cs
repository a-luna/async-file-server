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

        public async Task<Result> ExecuteAsync()
        {
            _state.DoNotRefreshMainMenu = true;
            var exit = false;
            Result result = null;

            while (!exit)
            {
                _state.DisplayCurrentStatus();

                var selectedOption = Menu.GetUserSelection(MenuText, MenuItems);
                result = await selectedOption.ExecuteAsync().ConfigureAwait(false);

                if (result.Success && !(selectedOption is ReturnToParentMenuItem))
                {
                    ApplyChanges();
                    exit = true;
                    continue;
                }

                exit = selectedOption.ReturnToParent;
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
