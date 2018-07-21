namespace AaronLuna.AsyncFileServer.Console.Menus.ServerConfigurationMenus.LocalServerSettingsMenuItems
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    
    using Common.Console.Menu;
    using Common.Result;

    using CommonMenuItems;

    class SetSocketListenBacklogSizeMenu : IMenu
    {
        readonly AppState _state;
        readonly List<int> _backlogSizeValues;

        public SetSocketListenBacklogSizeMenu(AppState state)
        {
            _state = state;

            ReturnToParent = true;
            ItemText = "*Listen Socket Backlog Limit.............: " +
                       $"{_state.Settings.SocketSettings.ListenBacklogSize} connections";
            MenuText = "Select a value from the list below:";

            // IMPORTANT: The size of the backlog parameter is limited by the OS, and a smaller value is preferred to improve performance.
            // Historically, the backlog size has been restricted to a maximum of 5, but modern systems have raised the cap to 100-200.
            _backlogSizeValues = new List<int>
            {
                1,
                2,
                5,
                10,
                25
            };

            MenuItems = new List<IMenuItem>
            {
                new SelectDummyValueMenuItem($"{_backlogSizeValues[0]:N0} connection"),
                new SelectDummyValueMenuItem($"{_backlogSizeValues[1]:N0} connections"),
                new SelectDummyValueMenuItem($"{_backlogSizeValues[2]:N0} connections"),
                new SelectDummyValueMenuItem($"{_backlogSizeValues[3]:N0} connections"),
                new SelectDummyValueMenuItem($"{_backlogSizeValues[4]:N0} connections{Environment.NewLine}"),
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
            if (menuIndex > _backlogSizeValues.Count) return Result.Ok();

            _state.Settings.SocketSettings.ListenBacklogSize = _backlogSizeValues[menuIndex - 1];
            _state.RestartRequired = true;

            return Result.Ok();
        }
    }
}
