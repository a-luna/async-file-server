namespace AaronLuna.AsyncFileServer.Console.Menus.ServerConfigurationMenus
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    using ServerConfigurationMenuItems;
    using Common.Console.Menu;
    using Common.Result;

    class SetTransferRetryLimitMenuItem : IMenu
    {
        readonly AppState _state;
        readonly List<int> _retryLimitValues;

        public SetTransferRetryLimitMenuItem(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = $"Change max download attempts for remote servers ({_state.Settings.TransferRetryLimit} attempts)";
            MenuText = "Select a value from the list below:";

            _retryLimitValues = new List<int>
            {
                1,
                3,
                5,
                10
            };

            MenuItems = new List<IMenuItem>
            {
                new SelectIntegerValueMenuItem($"{_retryLimitValues[0]} attempt"),
                new SelectIntegerValueMenuItem($"{_retryLimitValues[1]} attempts"),
                new SelectIntegerValueMenuItem($"{_retryLimitValues[2]} attempts"),
                new SelectIntegerValueMenuItem($"{_retryLimitValues[3]} attempts{Environment.NewLine}"),
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
            _state.DisplayCurrentStatus();

            var menuIndex = await SharedFunctions.GetUserSelectionIndexAsync(MenuText, MenuItems, _state);
            if (menuIndex > _retryLimitValues.Count) return Result.Ok();

            _state.Settings.TransferRetryLimit = _retryLimitValues[menuIndex - 1];

            return Result.Ok();
        }
    }
}
