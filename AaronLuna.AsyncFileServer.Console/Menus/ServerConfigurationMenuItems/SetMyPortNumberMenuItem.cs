namespace AaronLuna.AsyncFileServer.Console.Menus.ServerConfigurationMenuItems
{
    using System;
    using System.Threading.Tasks;

    using Common.Console.Menu;
    using Common.Result;

    class SetMyPortNumberMenuItem : IMenuItem
    {
        readonly AppState _state;

        public SetMyPortNumberMenuItem(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = $"Change local server port number * ({_state.Settings.LocalServerPortNumber})";
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public Task<Result> ExecuteAsync()
        {
            return Task.Run((Func<Result>)Execute);
        }

        Result Execute()
        {
            _state.UserEntryLocalServerPort =
                SharedFunctions.GetPortNumberFromUser(Resources.Prompt_SetLocalPortNumber, true);

            _state.RestartRequired = true;
            return Result.Ok();
        }
    }
}
