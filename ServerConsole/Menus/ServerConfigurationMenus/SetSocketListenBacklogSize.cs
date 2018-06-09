namespace ServerConsole.Menus.ServerConfigurationMenus
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Result;

    using ServerConfigurationMenuItems;

    class SetSocketListenBacklogSize : IMenu
    {
        readonly AppState _state;
        readonly List<int> _backlogSizeValues;

        public SetSocketListenBacklogSize(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = $"Change listen socket backlog size * ({_state.Settings.SocketSettings.ListenBacklogSize})";
            MenuText = "Select a value from the list below:";

            _backlogSizeValues = new List<int>
            {
                1,
                5,
                10,
                25,
                50,
                100
            };

            MenuItems = new List<IMenuItem>
            {
                new SelectIntegerValueMenuItem($"{_backlogSizeValues[0]:N0} connection"),
                new SelectIntegerValueMenuItem($"{_backlogSizeValues[1]:N0} connections"),
                new SelectIntegerValueMenuItem($"{_backlogSizeValues[2]:N0} connections"),
                new SelectIntegerValueMenuItem($"{_backlogSizeValues[3]:N0} connections"),
                new SelectIntegerValueMenuItem($"{_backlogSizeValues[4]:N0} connections"),
                new SelectIntegerValueMenuItem($"{_backlogSizeValues[5]:N0} connections{Environment.NewLine}"),
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
            if (menuIndex > _backlogSizeValues.Count) return Result.Ok();

            _state.Settings.SocketSettings.ListenBacklogSize = _backlogSizeValues[menuIndex - 1];
            _state.RestartRequired = true;

            return Result.Ok();
        }
    }
}
