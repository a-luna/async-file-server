using System.Linq;
using ServerConsole.Menus.ViewRequestQueueMenuItems;

namespace ServerConsole.Menus
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Logging;
    using AaronLuna.Common.Result;

    using MainMenuItems;

    class MainMenu : IMenu
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
            MenuItems = new List<IMenuItem>();
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }
        public string MenuText { get; set; }
        public List<IMenuItem> MenuItems { get; set; }

        public Task<Result> DisplayMenuAsync()
        {
            return Task.Run((Func<Result>) DisplayMenu);
        }

        Result DisplayMenu()
        {
            if (_state.DoNotRefreshMainMenu) return Result.Ok();
            _state.DisplayCurrentStatus();

            PopulateMenu();
            Menu.DisplayMenu(MenuText, MenuItems);

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

                if (_state.ErrorOccurred)
                {
                    var shutdown = SharedFunctions.PromptUserYesOrNo("Shutdown server?");
                    if (shutdown)
                    {
                        await _shutdownServer.ExecuteAsync();
                        exit = true;
                    }
                    else
                    {
                        _state.ErrorOccurred = false;
                    }

                    continue;
                }

                _state.DisplayCurrentStatus();
                PopulateMenu();
                
                var menuItem = await SharedFunctions.GetUserSelectionAsync(MenuText, MenuItems, _state);
                result = await menuItem.ExecuteAsync().ConfigureAwait(false);
                exit = menuItem.ReturnToParent;

                if (result.Success) continue;
                _log.Error($"Error: {result.Error}");
                Console.WriteLine($"{Environment.NewLine}Error: {result.Error}");
                await Task.Delay(_state.MessageDisplayTime);
            }

            if (!_state.ProgressBarInstantiated) return result;
            _state.ProgressBar.Dispose();
            _state.ProgressBarInstantiated = false;

            return result;
        }

        public void PopulateMenu()
        {
            MenuItems.Clear();
            if (_state.LocalServer.RequestQueue.Count > 0)
            {
                var oldestRequest = _state.LocalServer.RequestQueue.First();
                MenuItems.Add(new ProcessSelectedRequestMenuItem(_state, oldestRequest, true));
            }
            
            if (_state.ClientSelected)
            {
                MenuItems.Add(new SendTextMessageMenuItem(_state));
                MenuItems.Add(new SelectFileMenu(_state, true));
                MenuItems.Add(new SelectFileMenu(_state, false));
            }

            if (_state.LocalServer.StalledTransfers.Count > 0)
            {
                MenuItems.Add(new RetryStalledFileTransferMenu(_state));
            }

            if (_state.LocalServer.RequestQueue.Count > 1)
            {
                MenuItems.Add(new ViewRequestQueueMenu(_state));
            }

            if (_state.LocalServer.FileTransfers.Count > 0)
            {
                MenuItems.Add(new ViewFileTransferEventLogsMenu(_state));
            }

            MenuItems.Add(new SelectRemoteServerMenu(_state));
            MenuItems.Add(new ServerConfigurationMenu(_state));
            MenuItems.Add(_shutdownServer);
        }
    }
}
