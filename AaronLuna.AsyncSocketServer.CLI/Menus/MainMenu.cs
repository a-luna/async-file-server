﻿using System;
using System.Threading.Tasks;
using AaronLuna.AsyncSocketServer.CLI.Menus.EventLogsMenus;
using AaronLuna.AsyncSocketServer.CLI.Menus.EventLogsMenus.EventLogsMenuItems;
using AaronLuna.AsyncSocketServer.CLI.Menus.MainMenuItems;
using AaronLuna.AsyncSocketServer.CLI.Menus.PendingRequestsMenus;
using AaronLuna.AsyncSocketServer.CLI.Menus.PendingRequestsMenus.PendingRequestsMenuItems;
using AaronLuna.AsyncSocketServer.CLI.Menus.RemoteServerMenus;
using AaronLuna.AsyncSocketServer.CLI.Menus.RemoteServerMenus.RemoteServerMenuItems;
using AaronLuna.AsyncSocketServer.CLI.Menus.ServerConfigurationMenus;
using AaronLuna.Common.Console.Menu;
using AaronLuna.Common.Logging;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncSocketServer.CLI.Menus
{
    class MainMenu : ITieredMenu
    {
        readonly AppState _state;
        readonly ShutdownServerMenuItem _shutdownServer;
        readonly Logger _log = new Logger(typeof(MainMenu));

        public MainMenu(AppState state)
        {
            _state = state;
            _shutdownServer = new ShutdownServerMenuItem(state);

            ReturnToParent = true;
            ItemText = "Main menu";
            MenuText = "Main Menu:";
            TieredMenu = new TieredMenu();
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }
        public string MenuText { get; set; }
        public TieredMenu TieredMenu { get; set; }

        public Result DisplayMenu()
        {
            if (_state.DoNotRefreshMainMenu) return Result.Ok();
            SharedFunctions.DisplayLocalServerInfo(_state);

            PopulateMenu();
            ConsoleMenu.DisplayTieredMenu(TieredMenu);
            Console.WriteLine(ValidRangeOfMenuItemNumbers());

            return Result.Ok();
        }

        public async Task<Result> ExecuteAsync()
        {
            var exit = false;
            Result result = null;

            while (!exit)
            {
                _state.DoNotRefreshMainMenu = false;

                if (_state.RestartRequired)
                {
                    exit = true;
                    continue;
                }

                SharedFunctions.DisplayLocalServerInfo(_state);
                PopulateMenu();

                var menuItem = GetUserSelection();
                result = await menuItem.ExecuteAsync().ConfigureAwait(false);
                exit = menuItem.ReturnToParent;

                if (result.Success) continue;
                _state.DoNotRefreshMainMenu = true;

                Console.WriteLine(string.Empty);
                SharedFunctions.ModalMessage(result.Error, Resources.Prompt_PressEnterToContinue);
            }

            return result;
        }

        void PopulateMenu()
        {
            TieredMenu.Clear();

            PopulatePendingRequestsMenuTier();
            PopulateRemoteServerMenuTier();
            PopulateServerConfigurationMenuTier();
            PopulateViewLogsMenuTier();
        }

        IMenuItem GetUserSelection()
        {
            var userSelection = 0;
            while (userSelection == 0)
            {
                ConsoleMenu.DisplayTieredMenu(TieredMenu);
                Console.WriteLine(ValidRangeOfMenuItemNumbers());
                var input = Console.ReadLine();

                var inputValidation =
                    SharedFunctions.ValidateNumberIsWithinRange(input, 1, TieredMenu.ItemCount);

                if (inputValidation.Failure)
                {
                    SharedFunctions.ModalMessage(
                        inputValidation.Error,
                        Resources.Prompt_PressEnterToContinue);

                    SharedFunctions.DisplayLocalServerInfo(_state);
                    continue;
                }

                userSelection = inputValidation.Value;
            }

            return TieredMenu.GetMenuItem(userSelection - 1);
        }

        string ValidRangeOfMenuItemNumbers()
        {
            return $"Enter a menu item number (valid range 1-{TieredMenu.ItemCount}):";
        }

        void PopulatePendingRequestsMenuTier()
        {
            var pendingRequestsMenuTier = new MenuTier(Resources.MenuTierLabel_PendingRequests);

            if (_state.UnreadErrors.Count > 0)
            {
                pendingRequestsMenuTier.MenuItems.Add(
                    new ReadNewErrorMessagesMenuItem(_state, _state.UnreadErrors));
            }

            if (_state.PendingFileTransferCount > 0)
            {
                pendingRequestsMenuTier.MenuItems.Add(
                    new ViewPendingFileTransfersMenu(_state));
            }

            if (_state.StalledFileTransferIds.Count > 0)
            {
                pendingRequestsMenuTier.MenuItems.Add(
                    new ViewStalledFileTransfersMenu(_state));
            }

            if (_state.UnreadTextMessageCount > 0)
            {
                foreach (var id in _state.TextSessionIdsWithUnreadMessages)
                {
                    var textSession = _state.LocalServer.GetConversationById(id).Value;

                    pendingRequestsMenuTier.MenuItems.Add(
                        new ReadNewTextMessagesMenuItem(_state, textSession));
                }
            }

            TieredMenu.AddTier(pendingRequestsMenuTier);
        }

        void PopulateRemoteServerMenuTier()
        {
            var selectedServerMenuTier = new MenuTier(_state.RemoteServerInfo());

            if (_state.RemoteServerSelected)
            {
                selectedServerMenuTier.MenuItems.Add(new SendTextMessageMenuItem(_state));
                selectedServerMenuTier.MenuItems.Add(new SelectFileMenu(_state, true));
                selectedServerMenuTier.MenuItems.Add(new SelectFileMenu(_state, false));
                selectedServerMenuTier.MenuItems.Add(new EditServerInfoMenu(_state));
                selectedServerMenuTier.MenuItems.Add(new DeleteServerInfoMenuItem(_state));
            }

            TieredMenu.AddTier(selectedServerMenuTier);
        }

        void PopulateServerConfigurationMenuTier()
        {
            var serverConfigurationMenuTier =
                new MenuTier(Resources.MenuTierLabel_ServerConfiguration);

            serverConfigurationMenuTier.MenuItems.Add(new SelectRemoteServerMenu(_state));
            serverConfigurationMenuTier.MenuItems.Add(new LocalServerSettingsMenu(_state));
            serverConfigurationMenuTier.MenuItems.Add(new LocalServerNetworkPropertiesMenu(_state));
            serverConfigurationMenuTier.MenuItems.Add(_shutdownServer);

            TieredMenu.AddTier(serverConfigurationMenuTier);
        }

        void PopulateViewLogsMenuTier()
        {
            var viewLogsMenuTier = new MenuTier(Resources.MenuTierLabel_ViewLogs);

            if (!_state.NoFileTransfers)
            {
                viewLogsMenuTier.MenuItems.Add(new FileTransferLogsMenu(_state));
            }

            if (!_state.NoTextSessions)
            {
                viewLogsMenuTier.MenuItems.Add(new TextMessageLogsMenu(_state));
            }

            if (!_state.NoRequests)
            {
                viewLogsMenuTier.MenuItems.Add(new ServerRequestLogsMenu(_state));
            }

            viewLogsMenuTier.MenuItems.Add(new ViewAllEventsMenuItem(_state));
            viewLogsMenuTier.MenuItems.Add(new SetLogLevelMenu(_state));

            TieredMenu.AddTier(viewLogsMenuTier);
        }
    }
}
