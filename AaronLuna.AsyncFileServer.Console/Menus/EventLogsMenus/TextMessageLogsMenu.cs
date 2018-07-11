namespace AaronLuna.AsyncFileServer.Console.Menus.EventLogsMenus
{
    using System.Collections.Generic;
    using System.Threading.Tasks;

    using Common.Console.Menu;
    using Common.Result;

    using TextMessageLogsMenuItems;

    class TextMessageLogsMenu : IMenu
    {
        readonly AppState _state;

        public TextMessageLogsMenu(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = "Text message logs";
            MenuText = "Select a remote server from the list below:";
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
                PopulateMenu();
                SharedFunctions.DisplayLocalServerInfo(_state);
                var menuItem = await SharedFunctions.GetUserSelectionAsync(MenuText, MenuItems, _state).ConfigureAwait(false);
                exit = menuItem.ReturnToParent;
                result = await menuItem.ExecuteAsync().ConfigureAwait(false);
            }

            return result;
        }

        void PopulateMenu()
        {
            MenuItems.Clear(); 

            foreach (var id in _state.LocalServer.TextSessionIds)
            {
                var textSession = _state.LocalServer.GetTextSessionById(id).Value;
                MenuItems.Add(new ViewTextMessageLogMenuItem(_state, textSession));
            }

            MenuItems.Add(new ReturnToParentMenuItem("Return to main menu"));
        }
    }
}
