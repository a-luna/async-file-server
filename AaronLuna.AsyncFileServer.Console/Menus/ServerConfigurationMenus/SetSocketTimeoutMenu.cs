namespace AaronLuna.AsyncFileServer.Console.Menus.ServerConfigurationMenus
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    using ServerConfigurationMenuItems;
    using Common.Console.Menu;
    using Common.Result;

    class SetSocketTimeoutMenu : IMenu
    {
        readonly AppState _state;
        readonly List<int> _timeoutValues;

        public SetSocketTimeoutMenu(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = $"Change socket timeout value * ({_state.Settings.SocketSettings.SocketTimeoutInMilliseconds} ms)";
            MenuText = "Select a value from the list below:";

            _timeoutValues = new List<int>
            {
                1000,
                2000,
                3000,
                5000,
                10000,
                15000
            };

            MenuItems = new List<IMenuItem>
            {
                new SelectIntegerValueMenuItem("1 second"),
                new SelectIntegerValueMenuItem("2 seconds"),
                new SelectIntegerValueMenuItem("3 seconds"),
                new SelectIntegerValueMenuItem("5 seconds"),
                new SelectIntegerValueMenuItem("10 seconds"),
                new SelectIntegerValueMenuItem($"15 seconds{Environment.NewLine}"),
                new ReturnToParentMenuItem("Return to previous menu")
            };
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }
        public string MenuText { get; set; }
        public List<IMenuItem> MenuItems { get; set; }

        public async Task<Result> ExecuteAsync()
        {
            _state.DoNotRefreshMainMenu = true;
            SharedFunctions.DisplayLocalServerInfo(_state);

            var menuIndex = await SharedFunctions.GetUserSelectionIndexAsync(MenuText, MenuItems, _state);
            if (menuIndex > _timeoutValues.Count) return Result.Ok();

            _state.Settings.SocketSettings.SocketTimeoutInMilliseconds = _timeoutValues[menuIndex - 1];
            _state.RestartRequired = true;

            return Result.Ok();
        }
    }
}
