using System;
using System.Threading.Tasks;
using AaronLuna.Common.Console.Menu;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncFileServer.Console.Menus.UpdateSelectedServerInfoMenuItems
{
    class GetServerNameFromUserMenuItem : IMenuItem
    {
        readonly AppState _state;

        public GetServerNameFromUserMenuItem(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = $"Change the name ({_state.SelectedServerInfo.Name})";
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
                SharedFunctions.GetServerNameFromUser(Resources.Prompt_ChangeRemoteServerName);

            return Result.Ok();
        }
    }
}
