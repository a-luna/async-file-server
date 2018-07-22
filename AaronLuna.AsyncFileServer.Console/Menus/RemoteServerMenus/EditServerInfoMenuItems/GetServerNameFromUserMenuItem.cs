namespace AaronLuna.AsyncFileServer.Console.Menus.RemoteServerMenus.EditServerInfoMenuItems
{
    using System;
    using System.Threading.Tasks;

    using Common.Console.Menu;
    using Common.Result;

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
