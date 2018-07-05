namespace AaronLuna.AsyncFileServer.Console.Menus.MainMenuItems
{
    using System;
    using System.Threading.Tasks;

    using Common.Console.Menu;
    using Common.Result;

    class ProcessNextRequestInQueueMenuItem : IMenuItem
    {
        readonly AppState _state;

        public ProcessNextRequestInQueueMenuItem(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = "Process inbound file transfer";
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public async Task<Result> ExecuteAsync()
        {
            if (_state.LocalServer.NoFileTransfersPending)
            {
                return Result.Ok();
            }
            
            var result = await _state.LocalServer.ProcessNextRequestInQueueAsync();
            if (result.Failure)
            {
                return result;
            }
            
            Console.WriteLine($"{Environment.NewLine}Press enter to return to the main menu.");
            Console.ReadLine();

            _state.SignalReturnToMainMenu.Set();
            return Result.Ok();
        }
    }
}
