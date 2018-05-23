﻿namespace ServerConsole.Commands.Menus
{
    using System.Collections.Generic;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Result;

    using Getters;

    class ViewEventLogsMenu : MenuLoop, IMenuItem
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

        Task<Result> IMenuItem.ExecuteAsync()
        {
            return ExecuteAsync();
        }

        public new async Task<Result> ExecuteAsync()
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

                var selectedOption = Menu.GetUserSelection(MenuText, MenuItems);
                exit = selectedOption.ReturnToParent;
                result = await selectedOption.ExecuteAsync().ConfigureAwait(false);
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

            MenuItems.Add(new ReturnToParentMenuItem("Return to main menu"));
        }
    }
}
