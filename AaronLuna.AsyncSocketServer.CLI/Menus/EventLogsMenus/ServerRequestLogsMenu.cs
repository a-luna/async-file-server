using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AaronLuna.AsyncSocketServer.CLI.Menus.EventLogsMenus.ServerRequestLogsMenuItems;
using AaronLuna.Common.Console.Menu;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncSocketServer.CLI.Menus.EventLogsMenus
{
    class ServerRequestLogsMenu : IMenu
    {
        readonly AppState _state;
        readonly List<int> _requestIds;

        public ServerRequestLogsMenu(AppState state)
        {
            _state = state;
            _requestIds = _state.RequestIds;

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
                var menuItem = SharedFunctions.GetUserSelection(MenuText, MenuItems, _state);
                exit = menuItem.ReturnToParent;
                result = await menuItem.ExecuteAsync().ConfigureAwait(false);
            }

            return result;
        }

        bool NoRequestsToDisplay()
        {
            if (_state.NoRequests)
            {
                SharedFunctions.ModalMessage(
                    "There are no server request logs available",
                    Resources.Prompt_ReturnToPreviousMenu);

                return true;
            }

            var lastRequestTime = _state.MostRecentRequestTime;
            if (lastRequestTime > _state.LogViewerRequestBoundary) return false;

            const string prompt =
                "No server requests have been received since this list was cleared, would you like to view " +
                "event logs for all requests?";

            var restoreLogEntries = SharedFunctions.PromptUserYesOrNo(_state, prompt);
            if (!restoreLogEntries)
            {
                return true;
            }

            _state.LogViewerRequestBoundary = DateTime.MinValue;
            return false;
        }

        void PopulateMenu()
        {
            MenuItems.Clear();

            var requestsDesc =
                Enumerable.Range(0, _requestIds.Count)
                    .Select(i => _state.LocalServer.GetRequestById(_requestIds[i]).Value)
                    .Where(request => request.TimeStamp > _state.LogViewerRequestBoundary)
                    .OrderByDescending(r => r.TimeStamp)
                    .ToList();

            foreach (var i in Enumerable.Range(0, requestsDesc.Count))
            {
                var eventLog = _state.LocalServer.GetEventLogForRequest(requestsDesc[i].Id);

                SharedFunctions.LookupRemoteServerName(
                    requestsDesc[i].RemoteServerInfo,
                    _state.Settings.RemoteServers);

                var itemText = requestsDesc[i].ItemText(i + 1);

                MenuItems.Add(
                    new ServerRequestLogViewerMenuItem(
                        _state,
                        eventLog,
                        itemText));
            }

            MenuItems.Add(new ClearServerRequestLogsMenuItem(_state));
            MenuItems.Add(new ReturnToParentMenuItem("Return to main menu"));
        }
    }
}
