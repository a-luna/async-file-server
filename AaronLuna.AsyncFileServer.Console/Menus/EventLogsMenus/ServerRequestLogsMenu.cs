namespace AaronLuna.AsyncFileServer.Console.Menus.EventLogsMenus
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    using Common.Console.Menu;
    using Common.Result;

    using ServerRequestLogsMenuItems;

    class ServerRequestLogsMenu : IMenu
    {
        readonly AppState _state;
        readonly List<int> _requestIds;

        public ServerRequestLogsMenu(AppState state)
        {
            _state = state;
            _requestIds = _state.LocalServer.RequestIds;

            ReturnToParent = false;
            ItemText = "Server request logs";
            MenuText = "Select a server request from the list below:";
            MenuItems = new List<IMenuItem>();
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }
        public string MenuText { get; set; }
        public List<IMenuItem> MenuItems { get; set; }

        public async Task<Result> ExecuteAsync()
        {
            _state.DoNotRefreshMainMenu = true;
            var exit = false;
            Result result = null;

            while (!exit)
            {
                if (NoRequestsToDisplay())
                {
                    exit = true;
                    result = Result.Ok();
                    continue;
                }

                PopulateMenu();
                SharedFunctions.DisplayLocalServerInfo(_state);
                var menuItem = await SharedFunctions.GetUserSelectionAsync(MenuText, MenuItems, _state).ConfigureAwait(false);
                exit = menuItem.ReturnToParent;
                result = await menuItem.ExecuteAsync().ConfigureAwait(false);
            }

            return result;
        }

        bool NoRequestsToDisplay()
        {
            if (_state.LocalServer.NoRequests)
            {
                SharedFunctions.NotifyUserErrorOccurred("There are no server request logs available");
                return true;
            }

            var lastRequestId = _state.LocalServer.MostRecentRequestId;
            if (lastRequestId > _state.LogViewerRequestId) return false;

            const string prompt =
                "No server requests have been received since this list was cleared, would you like to view " +
                "event logs for all requests?";

            var restoreLogEntries = SharedFunctions.PromptUserYesOrNo(prompt);
            if (!restoreLogEntries)
            {
                return true;
            }

            _state.LogViewerRequestId = 0;
            return false;
        }

        void PopulateMenu()
        {
            MenuItems.Clear();

            foreach (var id in _requestIds)
            {
                if (id <= _state.LogViewerRequestId) continue;

                var request = _state.LocalServer.GetRequestById(id).Value;
                var eventLog = _state.LocalServer.GetEventLogForRequest(id);

                MenuItems.Add(
                    new ServerRequestLogViewerMenuItem(
                        _state,
                        request,
                        eventLog,
                        _state.Settings.LogLevel));
            }

            MenuItems.Reverse();
            MenuItems.Add(new ClearServerRequestLogsMenuItem(_state));
            MenuItems.Add(new ReturnToParentMenuItem("Return to main menu"));
        }
    }
}
