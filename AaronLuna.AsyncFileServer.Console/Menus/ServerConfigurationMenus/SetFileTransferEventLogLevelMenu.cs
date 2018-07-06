namespace AaronLuna.AsyncFileServer.Console.Menus.ServerConfigurationMenus
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    using ServerConfigurationMenuItems;
    using Model;
    using Common.Console.Menu;
    using Common.Result;

    class SetFileTransferEventLogLevelMenu : IMenu
    {
        readonly AppState _state;
        readonly List<FileTransferLogLevel> _logLevels;

        public SetFileTransferEventLogLevelMenu(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = $"Change file transfer event log level ({_state.LogLevel})";
            MenuText = "Select the appropriate log level from the list below:";

            _logLevels = new List<FileTransferLogLevel>
            {
                FileTransferLogLevel.Normal,
                FileTransferLogLevel.Debug
            };

            MenuItems = new List<IMenuItem>
            {
                new SelectDummyValueMenuItem(FileTransferLogLevel.Normal.ToString()),
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

            var menuIndex = await SharedFunctions.GetUserSelectionIndexAsync(MenuText, MenuItems, _state);
            if (menuIndex > _logLevels.Count) return Result.Ok();

            _state.LogLevel = _logLevels[menuIndex - 1];

            return Result.Ok();
        }
    }
}
