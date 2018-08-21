using System;
using System.Threading.Tasks;
using AaronLuna.Common.Console.Menu;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncSocketServer.CLI.Menus.RemoteServerMenus.RemoteServerMenuItems
{
    class DeleteServerInfoMenuItem : IMenuItem
    {
        readonly AppState _state;

        public DeleteServerInfoMenuItem(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = "Forget about this server";
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public Task<Result> ExecuteAsync()
        {
            return Task.Run((Func<Result>)Execute);
        }

        Result Execute()
        {
            var deleteServerInfo = SharedFunctions.PromptUserYesOrNo(_state, Resources.Prompt_DeleteSelectedServerInfo);
            if (!deleteServerInfo) return Result.Ok();

            _state.RemoteServerSelected = false;
            _state.Settings.RemoteServers.Remove(_state.SelectedServerInfo);

            return _state.SaveSettingsToFile();
        }
    }
}
