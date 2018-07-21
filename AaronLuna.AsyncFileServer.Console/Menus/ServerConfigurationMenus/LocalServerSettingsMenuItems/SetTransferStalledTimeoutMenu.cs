namespace AaronLuna.AsyncFileServer.Console.Menus.ServerConfigurationMenus.LocalServerSettingsMenuItems
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    
    using Common.Console.Menu;
    using Common.Result;

    using CommonMenuItems;

    class SetTransferStalledTimeoutMenu : IMenu
    {
        readonly AppState _state;
        readonly List<int> _timeoutValues;

        public SetTransferStalledTimeoutMenu(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = " Consider File Transfer Stalled After....: " +
                       $"{_state.Settings.FileTransferStalledTimeout.TotalSeconds} seconds";
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

            _state.Settings.FileTransferStalledTimeout = TimeSpan.FromMilliseconds(_timeoutValues[menuIndex - 1]);
            _state.RestartRequired = true;

            return Result.Ok();
        }
    }
}
