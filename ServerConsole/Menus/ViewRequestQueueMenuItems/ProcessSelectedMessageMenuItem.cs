namespace ServerConsole.Menus.ViewRequestQueueMenuItems
{
    using System;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Result;

    using TplSockets;

    class ProcessSelectedMessageMenuItem : IMenuItem
    {
        readonly AppState _state;
        readonly Message _message;

        public ProcessSelectedMessageMenuItem(AppState state, Message message)
        {
            _state = state;
            _message = message;

            ReturnToParent = false;
            ItemText = message.ToString();
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public async Task<Result> ExecuteAsync()
        {
            if (_state.LocalServer.QueueIsEmpty)
            {
                return Result.Ok();
            }
            
            var result = await _state.LocalServer.ProcessMessageFromQueueAsync(_message.Id);
            if (result.Failure)
            {
                return result;
            }

            _state.SignalReturnToMainMenu.WaitOne();
            _state.SignalReturnToMainMenu.Reset();

            Console.WriteLine($"{Environment.NewLine}Press enter to return to the main menu.");
            Console.ReadLine();

            _state.SignalReturnToMainMenu.Set();
            return Result.Ok();
        }
    }
}
