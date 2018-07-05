namespace AaronLuna.AsyncFileServer.Console.Menus
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    using MainMenuItems;
    using Common.Console.Menu;
    using Common.Logging;
    using Common.Result;

    class MainMenu : IMenu
    {
        readonly AppState _state;
        readonly ShutdownServerMenuItem _shutdownServer;
        readonly TieredMenu _tieredMenu;
        readonly Logger _log = new Logger(typeof(MainMenu));

        public MainMenu(AppState state)
        {
            _state = state;
            _tieredMenu = new TieredMenu();
            _shutdownServer = new ShutdownServerMenuItem(state);

            ReturnToParent = true;
            ItemText = "Main menu";
            MenuText = "Main Menu:";
            MenuItems = new List<IMenuItem>();
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }
        public string MenuText { get; set; }
        public List<IMenuItem> MenuItems { get; set; }
        
        public Result DisplayMenu()
        {
            if (_state.DoNotRefreshMainMenu) return Result.Ok();
            SharedFunctions.DisplayLocalServerInfo(_state);

            PopulateMenu();
            Menu.DisplayTieredMenu(_tieredMenu);
            Console.WriteLine($"Enter a menu item number (valid range 1-{_tieredMenu.Count}):");

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
            _tieredMenu.Clear();

            PopulateHandleRequestsMenuTier();
            PopulateViewLogsMenuTier();
            PopulateRemoteServerMenuTier();
            PopulateLocalServerMenuTier();
        }

        async Task<IMenuItem> GetUserSelectionAsync()
        {
            var userSelection = 0;
            while (userSelection == 0)
            {
                Menu.DisplayTieredMenu(_tieredMenu);
                Console.WriteLine($"Enter a menu item number (valid range 1-{_tieredMenu.Count}):");
                var input = Console.ReadLine();

                var validationResult = SharedFunctions.ValidateNumberIsWithinRange(input, 1, _tieredMenu.Count);
                if (validationResult.Failure)
                {
                    Console.WriteLine(Environment.NewLine + validationResult.Error);
                    await Task.Delay(_state.MessageDisplayTime);

                    SharedFunctions.DisplayLocalServerInfo(_state);
                    continue;
                }

                userSelection = validationResult.Value;
            }

            return _tieredMenu.GetMenuItem(userSelection - 1);
        }

        void PopulateHandleRequestsMenuTier()
        {
            var handleRequestsMenuTier = new MenuTier { TierLabel = "Pending requests:" };
            handleRequestsMenuTier.MenuItems = new List<IMenuItem>();

            if (!_state.LocalServer.NoFileTransfersPending)
            {
                handleRequestsMenuTier.MenuItems.Add(new ProcessNextRequestInQueueMenuItem(_state));
            }

            if (_state.LocalServer.StalledTransfersIds.Count > 0)
            {
                handleRequestsMenuTier.MenuItems.Add(new RetryStalledFileTransferMenu(_state));
            }

            if (_state.LocalServer.UnreadTextMessageCount > 0)
            {
                foreach (var id in _state.LocalServer.TextSessionIdsWithUnreadMessages)
                {
                    var textSession = _state.LocalServer.GetTextSessionById(id).Value;
                    handleRequestsMenuTier.MenuItems.Add(new ReadTextMessageMenuItem(_state, textSession));
                }
            }
            
            _tieredMenu.Add(handleRequestsMenuTier);
        }

        void PopulateViewLogsMenuTier()
        {
            var viewLogsMenuTier = new MenuTier { TierLabel = "View logs:" };
            viewLogsMenuTier.MenuItems = new List<IMenuItem>();
            
            if (!_state.LocalServer.NoFileTransfers)
            {
                viewLogsMenuTier.MenuItems.Add(new ViewFileTransferEventLogsMenu(_state));
            }

            if (!_state.LocalServer.NoTextSessions)
            {
                viewLogsMenuTier.MenuItems.Add(new ViewTextSessionsMenu(_state));
            }

            _tieredMenu.Add(viewLogsMenuTier);
        }

        void PopulateRemoteServerMenuTier()
        {
            var remoteServerMenuTier = new MenuTier { TierLabel = _state.RemoteServerInfo() };
            remoteServerMenuTier.MenuItems = new List<IMenuItem>();

            if (_state.ClientSelected)
            {
                remoteServerMenuTier.MenuItems.Add(new SendTextMessageMenuItem(_state));
                remoteServerMenuTier.MenuItems.Add(new SelectFileMenu(_state, true));
                remoteServerMenuTier.MenuItems.Add(new SelectFileMenu(_state, false));
            }
            
            _tieredMenu.Add(remoteServerMenuTier);
        }

        void PopulateLocalServerMenuTier()
        {
            var localServerMenuTier = new MenuTier
            {
                TierLabel = "Local server options:",
                MenuItems = new List<IMenuItem>
                {
                    new SelectRemoteServerMenu(_state),
                    new ServerConfigurationMenu(_state),
                    _shutdownServer
                }
            };
            
            _tieredMenu.Add(localServerMenuTier);
        }
    }
}
