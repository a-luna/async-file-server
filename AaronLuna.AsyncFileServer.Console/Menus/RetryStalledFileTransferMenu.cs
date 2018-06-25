namespace AaronLuna.AsyncFileServer.Console.Menus
{
    using System.Collections.Generic;
    using System.Threading.Tasks;

    using RetryStalledFileTransferMenuItems;
    using Common.Console.Menu;
    using Common.Result;

    class RetryStalledFileTransferMenu : IMenu
    {
        readonly AppState _state;

        public RetryStalledFileTransferMenu(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = "Retry stalled file transfers";
            MenuText = "Choose a stalled file transfer below to attempt downloading:";
            MenuItems = new List<IMenuItem>();
        }

        public bool ReturnToParent { get; set; }
        public string ItemText { get; set; }
        public string MenuText { get; set; }
        public List<IMenuItem> MenuItems { get; set; }
        
        public async Task<Result> ExecuteAsync()
        {
            if (_state.LocalServer.StalledTransfersIds.Count == 0)
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
            foreach (var id in _state.LocalServer.StalledTransfersIds)
            {
                var fileTransfer = _state.LocalServer.GetFileTransferById(id).Value;
                MenuItems.Add(new RetryStalledFileTransferMenuItem(_state, fileTransfer.FileTransfer));
            }

            MenuItems.Add(new ReturnToParentMenuItem("Return to main menu"));
        }
    }
}
