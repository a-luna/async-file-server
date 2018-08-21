using System;
using System.Threading.Tasks;
using AaronLuna.Common.Console.Menu;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncSocketServer.CLI.Menus.RemoteServerMenus.EditServerInfoMenuItems
{
    class GetServerNameFromUserMenuItem : IMenuItem
    {
        readonly AppState _state;

        public GetServerNameFromUserMenuItem(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = $"Name........: {_state.SelectedServerInfo.Name}";
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public Task<Result> ExecuteAsync()
        {
            return Task.Run((Func<Result>)Execute);
        }

        Result Execute()
        {
            _state.SelectedServerInfo.Name =
                SharedFunctions.GetServerNameFromUser(_state, Resources.Prompt_ChangeRemoteServerName);

            return Result.Ok();
        }
    }
}
