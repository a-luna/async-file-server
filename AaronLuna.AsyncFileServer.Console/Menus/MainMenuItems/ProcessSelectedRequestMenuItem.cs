namespace AaronLuna.AsyncFileServer.Console.Menus.MainMenuItems
{
    using System;
    using System.Threading.Tasks;

    using Model;
    using Common.Console.Menu;
    using Common.Result;

    class ProcessSelectedRequestMenuItem : IMenuItem
    {
        readonly AppState _state;
        readonly ServerRequest _request;

        public ProcessSelectedRequestMenuItem(AppState state, ServerRequest request, bool inMainMenu)
        {
            _state = state;
            _request = request;

            ReturnToParent = false;

            var mainMenuItemText = "Process request:" +
                                   $"{Environment.NewLine}{Environment.NewLine}{request}{Environment.NewLine}";

            ItemText = inMainMenu
                ? mainMenuItemText
                : request.ToString();
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public async Task<Result> ExecuteAsync()
        {
            if (_state.LocalServer.QueueIsEmpty)
            {
                return Result.Ok();
            }
            
            var result = await _state.LocalServer.ProcessRequestAsync(_request.Id);
            if (result.Failure)
            {
                return result;
            }

            _state.SignalReturnToMainMenu.WaitOne();
            _state.SignalReturnToMainMenu.Reset();

            System.Console.WriteLine($"{Environment.NewLine}Press enter to return to the main menu.");
            System.Console.ReadLine();

            _state.SignalReturnToMainMenu.Set();
            return Result.Ok();
        }
    }
}
