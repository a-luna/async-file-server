using System.Threading.Tasks;
using AaronLuna.Common.Console.Menu;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncSocketServer.CLI.Menus.RemoteServerMenus.RemoteServerMenuItems
{
    class SendTextMessageMenuItem : IMenuItem
    {
        readonly AppState _state;

        public SendTextMessageMenuItem(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = "Send text message";
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public Task<Result> ExecuteAsync()
        {
            return SharedFunctions.SendTextMessageAsync(_state, _state.SelectedServerInfo);
        }
    }
}
