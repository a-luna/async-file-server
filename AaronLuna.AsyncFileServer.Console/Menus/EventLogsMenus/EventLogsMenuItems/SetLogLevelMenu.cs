namespace AaronLuna.AsyncFileServer.Console.Menus.EventLogsMenus.EventLogsMenuItems
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    using Common.Console.Menu;
    using Common.Result;

    using CommonMenuItems;
    using Model;

    class SetLogLevelMenu : IMenu
    {
        readonly AppState _state;
        readonly List<LogLevel> _logLevels;

        public SetLogLevelMenu(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = $"Change log level ({_state.Settings.LogLevel})";
            MenuText = "Select the log level from the list below:";

            _logLevels = new List<LogLevel>
            {
                LogLevel.Info,
                LogLevel.Debug,
                LogLevel.Trace
            };

            MenuItems = new List<IMenuItem>
            {
                new SelectDummyValueMenuItem(nameof(LogLevel.Info)),
                new SelectDummyValueMenuItem(nameof(LogLevel.Debug)),
                new SelectDummyValueMenuItem($"{LogLevel.Trace}{Environment.NewLine}"),
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
            if (menuIndex > _logLevels.Count) return Result.Ok();

            _state.Settings.LogLevel = _logLevels[menuIndex - 1];

            return ServerSettings.SaveToFile(_state.Settings, _state.SettingsFilePath);
        }
    }
}
