namespace AaronLuna.AsyncFileServer.Console.Menus
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    using MainMenuItems;
    using Common.Console.Menu;
    using Common.Logging;
    using Common.Result;

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
            Menu = new TieredMenu();
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }
        public string MenuText { get; set; }
        public TieredMenu Menu { get; set; }
        
        public Result DisplayMenu()
        {
            if (_state.DoNotRefreshMainMenu) return Result.Ok();
            SharedFunctions.DisplayLocalServerInfo(_state);

            PopulateMenu();
            Common.Console.Menu.Menu.DisplayTieredMenu(Menu);
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
                
                var menuItem = await GetUserSelectionAsync();
                result = await menuItem.ExecuteAsync().ConfigureAwait(false);
                exit = menuItem.ReturnToParent;

                if (result.Success) continue;

                Console.WriteLine($"{Environment.NewLine}Error: {result.Error}");
                Console.WriteLine($"{Environment.NewLine}Press enter to return to the main menu.");
                Console.ReadLine();
            }

            return result;
        }

        void PopulateMenu()
        {
            Menu.Clear();

            PopulatePendingRequestsMenuTier();
            PopulateViewLogsMenuTier();
            PopulateSelectedServerMenuTier();
            PopulateLocalServerMenuTier();
        }

        async Task<IMenuItem> GetUserSelectionAsync()
        {
            var userSelection = 0;
            while (userSelection == 0)
            {
                Common.Console.Menu.Menu.DisplayTieredMenu(Menu);
                Console.WriteLine(ValidRangeOfMenuItemNumbers());
                var input = Console.ReadLine();

                var validationResult =
                    SharedFunctions.ValidateNumberIsWithinRange(input, 1, Menu.Count);

                if (validationResult.Failure)
                {
                    Console.WriteLine(Environment.NewLine + validationResult.Error);
                    await Task.Delay(_state.MessageDisplayTime);

                    SharedFunctions.DisplayLocalServerInfo(_state);
                    continue;
                }

                userSelection = validationResult.Value;
            }

            return Menu.GetMenuItem(userSelection - 1);
        }

        string ValidRangeOfMenuItemNumbers()
        {
            return $"Enter a menu item number (valid range 1-{Menu.Count}):";
        }

        void PopulatePendingRequestsMenuTier()
        {
            var handleRequestsMenuTier =
                new MenuTier
                {
                    TierLabel = Resources.MenuTierLabel_PendingRequests,
                    MenuItems = new List<IMenuItem>()
                };
            
            if (!_state.LocalServer.NoFileTransfersPending)
            {
                handleRequestsMenuTier.MenuItems.Add(
                    new ProcessNextRequestInQueueMenuItem(_state));
            }

            if (_state.LocalServer.StalledTransferIds.Count > 0)
            {
                handleRequestsMenuTier.MenuItems.Add(
                    new RetryStalledFileTransferMenu(_state));
            }

            if (_state.LocalServer.UnreadTextMessageCount > 0)
            {
                foreach (var id in _state.LocalServer.TextSessionIdsWithUnreadMessages)
                {
                    var textSession = _state.LocalServer.GetTextSessionById(id).Value;

                    handleRequestsMenuTier.MenuItems.Add(
                        new ReadTextMessageMenuItem(_state, textSession));
                }
            }
            
            Menu.Add(handleRequestsMenuTier);
        }

        void PopulateViewLogsMenuTier()
        {
            var viewLogsMenuTier = new MenuTier
            {
                TierLabel = Resources.MenuTierLabel_ViewLogs,
                MenuItems = new List<IMenuItem>()
            };

            if (!_state.LocalServer.NoFileTransfers)
            {
                viewLogsMenuTier.MenuItems.Add(new FileTransferLogViewerMenu(_state));
            }

            if (!_state.LocalServer.NoTextSessions)
            {
                viewLogsMenuTier.MenuItems.Add(new TextMessageArchiveMenu(_state));
            }

            Menu.Add(viewLogsMenuTier);
        }

        void PopulateSelectedServerMenuTier()
        {
            var selectedServerMenuTier = new MenuTier
            {
                TierLabel = _state.RemoteServerInfo(),
                MenuItems = new List<IMenuItem>()
            };

            if (_state.RemoteServerSelected)
            {
                selectedServerMenuTier.MenuItems.Add(new SendTextMessageMenuItem(_state));
                selectedServerMenuTier.MenuItems.Add(new SelectFileMenu(_state, true));
                selectedServerMenuTier.MenuItems.Add(new SelectFileMenu(_state, false));
                selectedServerMenuTier.MenuItems.Add(new UpdateSelectedServerInfoMenu(_state));
                selectedServerMenuTier.MenuItems.Add(new SelectRemoteServerMenu(_state));
            }
            
            Menu.Add(selectedServerMenuTier);
        }

        void PopulateLocalServerMenuTier()
        {
            var localServerMenuTier = new MenuTier
            {
                TierLabel = Resources.MenuTierLabel_LocalServerOptions,
                MenuItems = new List<IMenuItem>()
            };

            if (!_state.RemoteServerSelected)
            {
                localServerMenuTier.MenuItems.Add(new SelectRemoteServerMenu(_state));
            }

            localServerMenuTier.MenuItems.Add(new LocalServerSettingsMenu(_state));
            localServerMenuTier.MenuItems.Add(new SocketSettingsMenu(_state));
            localServerMenuTier.MenuItems.Add(new FileTransferSettingsMenu(_state));
            localServerMenuTier.MenuItems.Add(_shutdownServer);

            Menu.Add(localServerMenuTier);
        }
    }
}
