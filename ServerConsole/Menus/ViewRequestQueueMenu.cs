namespace ServerConsole.Menus
{
    using System.Collections.Generic;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Result;

    using ViewRequestQueueMenuItems;

    class ViewRequestQueueMenu : IMenu
    {
        readonly AppState _state;

        public ViewRequestQueueMenu(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = "View queued requests";
            MenuText = "The requests below are waiting to be processed:";
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
            if (_state.LocalServer.QueueIsEmpty)
            {
                return Result.Fail("Request queue is empty");
            }

            _state.DoNotRefreshMainMenu = true;
            _state.DisplayCurrentStatus();
            PopulateMenu();

            var menuItem = await SharedFunctions.GetUserSelectionAsync(MenuText, MenuItems, _state);
            return await menuItem.ExecuteAsync();
        }

        void PopulateMenu()
        {
            MenuItems.Clear();
            foreach (var message in _state.LocalServer.Queue)
            {
                MenuItems.Add(new ProcessSelectedMessageMenuItem(_state, message));
            }

            MenuItems.Add(new ReturnToParentMenuItem("Return to main menu"));
        }
    }
}
