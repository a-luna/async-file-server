namespace ServerConsole.Menus
{
    using System.Collections.Generic;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Result;

    using ViewEventLogsMenuItems;

    class ViewEventLogsMenu : IMenu
    {
        readonly AppState _state;

        public ViewEventLogsMenu(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = "View event logs for processed requests";
            MenuText = "Select a processed request from the list below:";
            MenuItems = new List<IMenuItem>();
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }
        public string MenuText { get; set; }
        public List<IMenuItem> MenuItems { get; set; }

        public Task<Result> DisplayMenuAsync()
        {
            return Task.Run(() => DisplayMenu());
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
            if (_state.LocalServer.Archive.Count == 0)
            {
                return Result.Fail("There are curently no logs to view");
            }

            _state.DoNotRefreshMainMenu = true;
            var exit = false;
            Result result = null;

            while (!exit)
            {
                _state.DisplayCurrentStatus();
                PopulateMenu();

                var menuItem = await SharedFunctions.GetUserSelectionAsync(MenuText, MenuItems, _state);
                exit = menuItem.ReturnToParent;
                result = await menuItem.ExecuteAsync();                
            }

            return result;
        }

        void PopulateMenu()
        {
            MenuItems.Clear();

            foreach (var message in _state.LocalServer.Archive)
            {
                MenuItems.Add(new GetEventLogsForArchivedRequestMenuItem(message));
            }

            MenuItems.Add(new ClearEventLogsMenuItem(_state));
            MenuItems.Add(new ReturnToParentMenuItem("Return to main menu"));
        }
    }
}
