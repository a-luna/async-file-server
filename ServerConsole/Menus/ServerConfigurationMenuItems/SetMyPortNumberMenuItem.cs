namespace ServerConsole.Menus.ServerConfigurationMenuItems
{
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Result;

    class SetMyPortNumberMenuItem : IMenuItem
    {
        readonly AppState _state;

        public SetMyPortNumberMenuItem(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = $"Local server port number * ({_state.Settings.LocalPort})";
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public Task<Result> ExecuteAsync()
        {
            return Task.Factory.StartNew(Execute);
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
