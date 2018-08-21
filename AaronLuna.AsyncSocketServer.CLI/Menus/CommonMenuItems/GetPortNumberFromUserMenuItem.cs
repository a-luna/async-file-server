using System;
using System.Threading.Tasks;
using AaronLuna.Common.Console.Menu;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncSocketServer.CLI.Menus.CommonMenuItems
{
    class GetPortNumberFromUserMenuItem : IMenuItem
    {
        readonly AppState _state;
        readonly ServerInfo _serverInfo;
        readonly bool _localPort;

        public GetPortNumberFromUserMenuItem(AppState state, ServerInfo serverInfo, bool localPort)
        {
            _state = state;
            _serverInfo = serverInfo;
            _localPort = localPort;

            ReturnToParent = localPort;
            ItemText = localPort
                ? $"*Local Server Port.......................: {_state.Settings.LocalServerPortNumber}"
                : $"Port........: {_state.SelectedServerInfo.PortNumber}{Environment.NewLine}";
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public Task<Result> ExecuteAsync()
        {
            return Task.Run((Func<Result>)Execute);
        }

        Result Execute()
        {
            var prompt = _localPort
                ? Resources.Prompt_SetLocalPortNumber
                : Resources.Prompt_ChangeRemoteServerPortNumber;

            _serverInfo.PortNumber =
                SharedFunctions.GetPortNumberFromUser(_state, prompt, _localPort);

            _state.RestartRequired = _localPort;

            return Result.Ok();
        }
    }
}
