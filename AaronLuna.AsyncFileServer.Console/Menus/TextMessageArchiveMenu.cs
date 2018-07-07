namespace AaronLuna.AsyncFileServer.Console.Menus
{
    using System.Collections.Generic;
    using System.Threading.Tasks;

    using Common.Console.Menu;
    using Common.Result;

    using ViewTextSessionsMenuItems;

    class TextMessageArchiveMenu : IMenu
    {
        AppState _state;

        public TextMessageArchiveMenu(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = "View archived text messages";
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
                var menuItem = await SharedFunctions.GetUserSelectionAsync(MenuText, MenuItems, _state);
                exit = menuItem.ReturnToParent;
                result = await menuItem.ExecuteAsync();
            }

            return result;
        }

        void PopulateMenu()
        {
            MenuItems.Clear();

            foreach (var id in _state.LocalServer.TextSessionIds)
            {
                var textSession = _state.LocalServer.GetTextSessionById(id).Value;
                MenuItems.Add(new ViewTextSessionMenuItem(textSession));
            }
            
            MenuItems.Add(new ReturnToParentMenuItem("Return to main menu"));
        }
    }
}
