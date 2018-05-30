namespace ServerConsole.Menus
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Result;

    using SelectServerActionMenuItems;

    class SelectServerActionMenu : IMenu
    {
        readonly AppState _state;

        public SelectServerActionMenu(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = "Remote server options";
            MenuText = "Please make a choice from the menu below:";
            MenuItems = new List<IMenuItem>();

            var sendTextMessage = new SendTextMessageMenuItem(state);
            var sendFile = new SelectFileMenu(state, true);
            var getFile = new SelectFileMenu(state, false);
            var returnToMainMenu = new ReturnToParentMenuItem("Return to main menu");

            MenuItems.Add(sendTextMessage);
            MenuItems.Add(sendFile);
            MenuItems.Add(getFile);
            MenuItems.Add(returnToMainMenu);
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
            Menu.DisplayMenu(MenuText, MenuItems);

            return Result.Ok();
        }

        public async Task<Result> ExecuteAsync()
        {
            _state.DoNotRefreshMainMenu = true;
            _state.DisplayCurrentStatus();

            var menuItem = await SharedFunctions.GetUserSelectionAsync(MenuText, MenuItems, _state);

            var serverActionResult = await menuItem.ExecuteAsync();
            if (serverActionResult.Failure && menuItem is SelectFileMenu)
            {
                Console.WriteLine(serverActionResult.Error);
                Console.WriteLine($"{Environment.NewLine}Press enter to return to the main menu.");
                Console.ReadLine();
                return Result.Ok();
            }

            return serverActionResult;
        }
    }
}
