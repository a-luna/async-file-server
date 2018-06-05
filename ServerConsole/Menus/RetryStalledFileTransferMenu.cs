namespace ServerConsole.Menus
{
    using System.Collections.Generic;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Result;

    using RetryStalledFileTransferMenuItems;

    class RetryStalledFileTransferMenu : IMenu
    {
        readonly AppState _state;

        public RetryStalledFileTransferMenu(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = $"Retry stalled file transfers";
            MenuText = "Choose a stalled file transfer below to attempt downloading:";
            MenuItems = new List<IMenuItem>();
        }

        public bool ReturnToParent { get; set; }
        public string ItemText { get; set; }
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
            if (_state.LocalServer.StalledTransfers.Count == 0)
            {
                return Result.Fail("There are no stalled file transfers");
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
            foreach (var fileTransfer in _state.LocalServer.StalledTransfers)
            {
                MenuItems.Add(new RetryStalledFileTransferMenuItem(_state, fileTransfer));
            }

            MenuItems.Add(new ReturnToParentMenuItem("Return to main menu"));
        }
    }
}
