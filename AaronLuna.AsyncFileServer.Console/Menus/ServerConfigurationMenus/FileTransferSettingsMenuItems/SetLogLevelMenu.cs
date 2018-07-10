namespace AaronLuna.AsyncFileServer.Console.Menus.ServerConfigurationMenus.FileTransferSettingsMenuItems
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    using Common.Console.Menu;
    using Common.Result;

    using Model;
    using CommonMenuItems;

    class SetLogLevelMenu : IMenu
    {
        readonly AppState _state;
        readonly List<FileTransferLogLevel> _logLevels;

        public SetLogLevelMenu(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = $"Change file transfer event log level ({_state.LogLevel}){Environment.NewLine}";
            MenuText = "Select the appropriate log level from the list below:";

            _logLevels = new List<FileTransferLogLevel>
            {
                FileTransferLogLevel.Normal,
                FileTransferLogLevel.Debug
            };

            MenuItems = new List<IMenuItem>
            {
                new SelectDummyValueMenuItem(nameof(FileTransferLogLevel.Normal)),
                new SelectDummyValueMenuItem($"{FileTransferLogLevel.Debug}{Environment.NewLine}"),
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
            if (menuIndex > _logLevels.Count) return Result.Ok();

            _state.LogLevel = _logLevels[menuIndex - 1];

            return Result.Ok();
        }
    }
}
