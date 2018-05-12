namespace ServerConsole.Commands.ServerCommands
{
    using System;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Logging;
    using AaronLuna.Common.Result;

    class SendTextMessageCommand : ICommand
    {
        readonly AppState _state;
        readonly Logger _log = new Logger(typeof(SendTextMessageCommand));

        public SendTextMessageCommand(AppState state)
        {
            _log.Info("Begin: Instantiate SendTextMessageCommand");

            ReturnToParent = false;
            ItemText = "Send text message";

            _state = state;

            _log.Info("Complete: Instantiate SendTextMessageCommand");
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }
        public async Task<Result> ExecuteAsync()
        {
            _log.Info("Begin: SendTextMessageCommand.ExecuteAsync");

            var ipAddress = _state.ClientSessionIpAddress;
            var port = _state.ClientServerPort;

            Console.Clear();
            Console.WriteLine($"Please enter a text message to send to {ipAddress}:{port}");
            var message = Console.ReadLine();

            var sendMessageResult =
                await _state.LocalServer.SendTextMessageAsync(
                    message,
                    ipAddress.ToString(),
                    port).ConfigureAwait(false);

            if (sendMessageResult.Failure)
            {
                _log.Error($"Error: {sendMessageResult.Error} (SendTextMessageCommand.ExecuteAsync)");
            }

            _log.Info("Complete: SendTextMessageCommand.ExecuteAsync");

            return sendMessageResult.Success
                ? Result.Ok()
                : Result.Fail(sendMessageResult.Error);
        }
    }
}
