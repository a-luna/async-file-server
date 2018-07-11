namespace AaronLuna.AsyncFileServer.Console.Menus.ServerConfigurationMenus.FileTransferSettingsMenuItems
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    using Common.Console.Menu;
    using Common.Result;

    using CommonMenuItems;

    class SetRetryLockoutTimeSpanMenu : IMenu
    {
        readonly AppState _state;
        readonly List<int> _timeoutValues;

        public SetRetryLockoutTimeSpanMenu(AppState state)
        {
            _state = state;

            ReturnToParent = false;

            ItemText =
                "Change length of time to reject transfers after retry limit exceeded " +
                $"({_state.Settings.RetryLimitLockout.Minutes} minutes){Environment.NewLine}";

            MenuText = "Select a value from the list below:";

            _timeoutValues = new List<int>
            {
                5,
                10,
                15,
                30,
                60,
                120
            };

            MenuItems = new List<IMenuItem>
            {
                new SelectDummyValueMenuItem($"{_timeoutValues[0]} minutes"),
                new SelectDummyValueMenuItem($"{_timeoutValues[1]} minutes"),
                new SelectDummyValueMenuItem($"{_timeoutValues[2]} minutes"),
                new SelectDummyValueMenuItem($"{_timeoutValues[3]} minutes"),
                new SelectDummyValueMenuItem($"{_timeoutValues[4]} minutes"),
                new SelectDummyValueMenuItem($"{_timeoutValues[5]} minutes{Environment.NewLine}"),
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

            var menuIndex = await SharedFunctions.GetUserSelectionIndexAsync(MenuText, MenuItems, _state).ConfigureAwait(false);
            if (menuIndex > _timeoutValues.Count) return Result.Ok();

            _state.Settings.RetryLimitLockout = TimeSpan.FromMinutes(_timeoutValues[menuIndex - 1]);
            _state.RestartRequired = true;

            return Result.Ok();
        }
    }
}
