using System;
using System.Threading.Tasks;
using AaronLuna.Common.Console.Menu;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncSocketServer.CLI.Menus.RemoteServerMenus.SelectRemoteServerMenuItems
{
    class GetSelectedRemoteServerMenuItem : IMenuItem
    {
        readonly AppState _state;
        readonly ServerInfo _server;

        public GetSelectedRemoteServerMenuItem(AppState state, ServerInfo server)
        {
            _state = state;
            _server = server;

            ReturnToParent = false;
            ItemText = server.ItemText;
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public Task<Result> ExecuteAsync()
        {
            return Task.Run((Func<Result>) Execute);
        }

        Result Execute()
        {
            if (string.IsNullOrEmpty(_server.Name))
            {
                _server.Name = SharedFunctions.SetSelectedServerName(_state, _server);

                var saveSettings =_state.SaveSettingsToFile();
                if (saveSettings.Failure)
                {
                    return saveSettings;
                }
            }

            _state.SelectedServerInfo = _server;
            _state.RemoteServerSelected = true;

            return Result.Ok();

        }
    }
}
