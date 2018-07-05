namespace AaronLuna.AsyncFileServer.Console.Menus
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    using ViewFileTransferEventLogsMenuItems;
    using Common.Console.Menu;
    using Common.Result;

    class ViewFileTransferEventLogsMenu : IMenu
    {
        readonly AppState _state;

        public ViewFileTransferEventLogsMenu(AppState state)
        {
            _state = state;

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
                if (!RestoreEventLogsIfPreviouslyCleared())
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

        bool RestoreEventLogsIfPreviouslyCleared()
        {
            if (_state.LocalServer.NoFileTransfers)
            {
                Console.WriteLine("There are no file transfer event logs available");
                Console.WriteLine($"{Environment.NewLine}Press enter to return to the previous menu.");
                Console.ReadLine();

                return false;
            }

            var lastTransferId = _state.LocalServer.NewestTransferId;
            if (lastTransferId > _state.LogViewerFileTransferId) return true;

            const string prompt =
                "No file transfers have occurred since this list was cleared, would you like to view " +
                "event logs for all file transfers?";

            var restoreLogEntries = SharedFunctions.PromptUserYesOrNo(prompt);
            if (!restoreLogEntries)
            {
                return false;
            }

            _state.LogViewerFileTransferId = 0;
            return true;
        }

        void PopulateMenu()
        {
            MenuItems.Clear();

            foreach (var id in _state.LocalServer.FileTransferIds)
            {
                if (id <= _state.LogViewerFileTransferId) continue;

                var fileTransfer = _state.LocalServer.GetFileTransferById(id).Value;
                var eventLog = _state.LocalServer.GetEventLogForFileTransfer(id);

                MenuItems.Add(new GetFileTransferEventLogsMenuItem(fileTransfer.FileTransfer, eventLog));
            }

            MenuItems.Add(new ClearFileTransferEventLogsMenuItem(_state));
            MenuItems.Add(new ReturnToParentMenuItem("Return to main menu"));
        }
    }
}
