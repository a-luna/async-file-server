namespace AaronLuna.AsyncFileServer.Console.Menus.EventLogsMenus
{
    using System.Collections.Generic;
    using System.Threading.Tasks;

    using Common.Console.Menu;
    using Common.Result;

    using Model;
    using FileTransferLogsMenuItems;

    class FileTransferLogsMenu : IMenu
    {
        readonly AppState _state;
        readonly List<int> _fileTransferIds;

        public FileTransferLogsMenu(AppState state)
        {
            _state = state;
            _fileTransferIds = _state.LocalServer.FileTransferIds;

            ReturnToParent = false;
            ItemText = "File transfer logs";
            MenuText = "Select a file transfer from the list below:";
            MenuItems = new List<IMenuItem>();
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }
        public string MenuText { get; set; }
        public List<IMenuItem> MenuItems { get; set; }

        public async Task<Result> ExecuteAsync()
        {
            _state.DoNotRefreshMainMenu = true;
            var exit = false;
            Result result = null;

            while (!exit)
            {
                if (NoFileTransfersToDisplay())
                {
                    exit = true;
                    result = Result.Ok();
                    continue;
                }

                PopulateMenu();
                SharedFunctions.DisplayLocalServerInfo(_state);
                var menuItem = SharedFunctions.GetUserSelection(MenuText, MenuItems, _state);
                exit = menuItem.ReturnToParent;
                result = await menuItem.ExecuteAsync().ConfigureAwait(false);
            }

            return result;
        }

        bool NoFileTransfersToDisplay()
        {
            if (_state.LocalServer.NoFileTransfers)
            {
                SharedFunctions.NotifyUserErrorOccurred("There are no file transfer logs available");
                return true;
            }

            var lastTransferId = _state.LocalServer.MostRecentFileTransferId;
            if (lastTransferId > _state.LogViewerFileTransferId) return false;

            const string prompt =
                "No file transfers have occurred since this list was cleared, would you like to view " +
                "event logs for all transfers?";

            var restoreLogEntries = SharedFunctions.PromptUserYesOrNo(_state, prompt);
            if (!restoreLogEntries)
            {
                return true;
            }

            _state.LogViewerFileTransferId = 0;
            return false;
        }

        void PopulateMenu()
        {
            MenuItems.Clear();

            foreach (var id in _fileTransferIds)
            {
                if (id <= _state.LogViewerFileTransferId) continue;

                var fileTransferController = _state.LocalServer.GetFileTransferById(id).Value;
                var eventLog = _state.LocalServer.GetEventLogForFileTransfer(id, _state.Settings.LogLevel);

                SharedFunctions.LookupRemoteServerName(
                    fileTransferController.RemoteServerInfo,
                    _state.Settings.RemoteServers);

                if (_state.Settings.LogLevel == LogLevel.Info)
                {
                    eventLog.RemoveAll(LogLevelIsDebugOnly);
                }

                MenuItems.Add(
                    new FileTransferLogViewerMenuItem(
                        _state,
                        fileTransferController,
                        eventLog));
            }

            MenuItems.Reverse();
            MenuItems.Add(new ClearFileTransferEventLogsMenuItem(_state));
            MenuItems.Add(new ReturnToParentMenuItem("Return to main menu"));
        }

        static bool LogLevelIsDebugOnly(ServerEvent serverEvent)
        {
            return serverEvent.LogLevelIsDebugOnly;
        }
    }
}
