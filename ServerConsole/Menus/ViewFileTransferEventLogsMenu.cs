namespace ServerConsole.Menus
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Result;

    using ViewFileTransferEventLogsMenuItems;

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

        public Task<Result> DisplayMenuAsync()
        {
            return Task.Run((Func<Result>) DisplayMenu);
        }

        public Result DisplayMenu()
        {
            _state.DisplayCurrentStatus();
            PopulateMenu();
            Menu.DisplayMenu(MenuText, MenuItems);

            return Result.Ok();
        }
        
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
                _state.DisplayCurrentStatus();
                var menuItem = await SharedFunctions.GetUserSelectionAsync(MenuText, MenuItems, _state);
                exit = menuItem.ReturnToParent;
                result = await menuItem.ExecuteAsync();
            }

            return result;
        }

        bool RestoreEventLogsIfPreviouslyCleared()
        {
            if (_state.LocalServer.FileTransfers.Count == 0)
            {
                Console.WriteLine("There are no file transfer event logs available");
                Console.WriteLine($"{Environment.NewLine}Press enter to return to the previous menu.");
                Console.ReadLine();

                return false;
            }

            var lastTransferId = _state.LocalServer.FileTransfers.Last().Id;
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

            foreach (var fileTransfer in _state.LocalServer.FileTransfers)
            {
                if (fileTransfer.Id <= _state.LogViewerFileTransferId) continue;
                MenuItems.Add(new GetFileTransferEventLogsMenuItem(fileTransfer));
            }

            MenuItems.Add(new ClearFileTransferEventLogsMenuItem(_state));
            MenuItems.Add(new ReturnToParentMenuItem("Return to main menu"));
        }
    }
}
