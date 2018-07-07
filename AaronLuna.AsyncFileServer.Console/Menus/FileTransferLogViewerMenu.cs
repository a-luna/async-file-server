namespace AaronLuna.AsyncFileServer.Console.Menus
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;

    using ViewFileTransferEventLogsMenuItems;
    using Common.Console.Menu;
    using Common.Result;
    using Model;

    class FileTransferLogViewerMenu : IMenu
    {
        readonly AppState _state;
        readonly List<int> _fileTransferIds;

        public FileTransferLogViewerMenu(AppState state)
        {
            _state = state;
            _fileTransferIds = _state.LocalServer.FileTransferIds;

            ReturnToParent = false;
            ItemText = "View file transfer event logs";
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
                var menuItem = await SharedFunctions.GetUserSelectionAsync(MenuText, MenuItems, _state);
                exit = menuItem.ReturnToParent;
                result = await menuItem.ExecuteAsync();
            }

            return result;
        }

        bool NoFileTransfersToDisplay()
        {
            if (_state.LocalServer.NoFileTransfers)
            {
                Console.WriteLine("There are no file transfer event logs available");
                Console.WriteLine($"{Environment.NewLine}Press enter to return to the previous menu.");
                Console.ReadLine();

                return true;
            }

            var lastTransferId = _fileTransferIds.Last();
            if (lastTransferId > _state.LogViewerFileTransferId) return false;

            const string prompt =
                "No file transfers have occurred since this list was cleared, would you like to view " +
                "event logs for all file transfers?";

            var restoreLogEntries = SharedFunctions.PromptUserYesOrNo(prompt);
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
                var eventLog = _state.LocalServer.GetEventLogForFileTransfer(id);

                if (_state.LogLevel == FileTransferLogLevel.Normal)
                {
                    eventLog.RemoveAll(LogLevelIsDebugOnly);
                }

                MenuItems.Add(
                    new FileTransferLogViewerMenuItem(
                        fileTransferController,
                        eventLog,
                        _state.LogLevel));
            }

            MenuItems.Add(new ClearFileTransferEventLogsMenuItem(_state));
            MenuItems.Add(new ReturnToParentMenuItem("Return to main menu"));
        }

        static bool LogLevelIsDebugOnly(ServerEvent serverEvent)
        {
            return serverEvent.LogLevelIsDebugOnly;
        }
    }
}
