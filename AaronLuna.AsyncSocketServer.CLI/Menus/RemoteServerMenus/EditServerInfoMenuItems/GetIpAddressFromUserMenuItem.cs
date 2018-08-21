using System;
using System.Threading.Tasks;
using AaronLuna.Common.Console.Menu;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncSocketServer.CLI.Menus.RemoteServerMenus.EditServerInfoMenuItems
{
    class GetIpAddressFromUserMenuItem : IMenuItem
    {
        readonly AppState _state;

        public GetIpAddressFromUserMenuItem(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = $"IP Address..: {_state.SelectedServerInfo.SessionIpAddress}";
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public Task<Result> ExecuteAsync()
        {
            return Task.Run((Func<Result>)Execute);
        }

        Result Execute()
        {
            _state.SelectedServerInfo.SessionIpAddress =
                SharedFunctions.GetIpAddressFromUser(_state, Resources.Prompt_ChangeRemoteServerIp);

            return Result.Ok();
        }
    }
}
