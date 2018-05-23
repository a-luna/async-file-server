namespace ServerConsole.Commands.Menus
{
    using System.Collections.Generic;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Result;

    using ServerCommands;

    class SelectServerActionMenu : MenuSingleChoice, IMenuItem
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

        Task<Result> IMenuItem.ExecuteAsync()
        {
            return ExecuteAsync();
        }

        public new Task<Result> ExecuteAsync()
        {
            _state.DoNotRefreshMainMenu = true;
            _state.DisplayCurrentStatus();

            var menuItem = Menu.GetUserSelection(MenuText, MenuItems);
            return menuItem.ExecuteAsync();
        }
    }
}
