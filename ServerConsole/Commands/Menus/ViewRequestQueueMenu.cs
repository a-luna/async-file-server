namespace ServerConsole.Commands.Menus
{
    using System.Collections.Generic;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Result;

    using ServerCommands;

    class ViewRequestQueueMenu : MenuSingleChoice, IMenuItem
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

        Task<Result> IMenuItem.ExecuteAsync()
        {
            return ExecuteAsync();
        }

        public new async Task<Result> ExecuteAsync()
        {
            if (_state.LocalServer.QueueIsEmpty)
            {
                return Result.Fail("Request queue is empty");
            }

            _state.DoNotRefreshMainMenu = true;
            _state.DisplayCurrentStatus();
            PopulateMenu();

            var selectedOption = Menu.GetUserSelection(MenuText, MenuItems);
            return await selectedOption.ExecuteAsync().ConfigureAwait(false);
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
