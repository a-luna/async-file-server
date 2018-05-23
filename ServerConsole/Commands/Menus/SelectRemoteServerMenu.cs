﻿namespace ServerConsole.Commands.Menus
{
    using System.Collections.Generic;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Result;

    using CompositeCommands;
    using Getters;

    class SelectRemoteServerMenu : MenuSingleChoice, IMenuItem
    {
        readonly AppState _state;

        public SelectRemoteServerMenu(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = "Select remote server";
            MenuText = "Choose a remote server:";
            MenuItems = new List<IMenuItem>();
        }

        Task<Result> IMenuItem.ExecuteAsync()
        {
            return ExecuteAsync();
        }

        public new async Task<Result> ExecuteAsync()
        {
            _state.DoNotRefreshMainMenu = true;

            _state.DisplayCurrentStatus();
            PopulateMenu();

            var selectedOption = Menu.GetUserSelection(MenuText, MenuItems);
            var selectRemoteServerResult = await selectedOption.ExecuteAsync();
            if (selectRemoteServerResult.Success && !(selectedOption is ReturnToParentMenuItem))
            {
                _state.ClientSelected = true;
            }

            return selectRemoteServerResult;
        }

        void PopulateMenu()
        {
            MenuItems.Clear();

            foreach (var server in _state.Settings.RemoteServers)
            {
                MenuItems.Add(new GetSelectedRemoteServerMenuItem(_state, server));
            }

            MenuItems.Add(new GetRemoteServerInfoFromUserMenuItem(_state));
            MenuItems.Add(new ReturnToParentMenuItem("Return to main menu"));
        }
    }
}
