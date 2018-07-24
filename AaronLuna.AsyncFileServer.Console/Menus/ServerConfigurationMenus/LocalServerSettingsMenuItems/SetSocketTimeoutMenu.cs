namespace AaronLuna.AsyncFileServer.Console.Menus.ServerConfigurationMenus.LocalServerSettingsMenuItems
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    
    using Common.Console.Menu;
    using Common.Result;

    using CommonMenuItems;

    class SetSocketTimeoutMenu : IMenu
    {
        readonly AppState _state;
        readonly List<int> _timeoutValues;

        public SetSocketTimeoutMenu(AppState state)
        {
            _state = state;

            ReturnToParent = true;

            var timeout = TimeSpan.FromMilliseconds(_state.Settings.SocketSettings.SocketTimeoutInMilliseconds).TotalSeconds;

            ItemText =
                $"*Send/Receive Socket Timeout Length......: {timeout} seconds" +
                Environment.NewLine;

            MenuText = $"Select a value from the list below:{Environment.NewLine}";

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
                new SelectDummyValueMenuItem("1 second"),
                new SelectDummyValueMenuItem("2 seconds"),
                new SelectDummyValueMenuItem("3 seconds"),
                new SelectDummyValueMenuItem("5 seconds"),
                new SelectDummyValueMenuItem("10 seconds"),
                new SelectDummyValueMenuItem($"15 seconds{Environment.NewLine}"),
                new ReturnToParentMenuItem("Return to previous menu")
            };
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }
        public string MenuText { get; set; }
        public List<IMenuItem> MenuItems { get; set; }

        public Task<Result> ExecuteAsync()
        {
            return Task.Run((Func<Result>)Execute);
        }

        Result Execute()
        {
            _state.DoNotRefreshMainMenu = true;
            SharedFunctions.DisplayLocalServerInfo(_state);

            var menuIndex = SharedFunctions.GetUserSelectionIndex(MenuText, MenuItems, _state);
            if (menuIndex > _timeoutValues.Count) return Result.Ok();

            _state.Settings.SocketSettings.SocketTimeoutInMilliseconds = _timeoutValues[menuIndex - 1];
            _state.RestartRequired = true;

            return Result.Ok();
        }
    }
}
