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
            ReturnToParent = false;
            ItemText = "Send text message";

            _state = state;
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }
        public async Task<Result> ExecuteAsync()
        {
            var ipAddress = _state.RemoteServerInfo.SessionIpAddress.ToString();
            var port = _state.RemoteServerInfo.Port;

            Console.WriteLine($"Please enter a text message to send to {ipAddress}:{port}");
            var message = Console.ReadLine();

            var sendMessageResult =
                await _state.LocalServer.SendTextMessageAsync(
                    message,
                    ipAddress,
                    port).ConfigureAwait(false);

            if (sendMessageResult.Failure)
            {
                _log.Error($"Error: {sendMessageResult.Error} (SendTextMessageCommand.ExecuteAsync)");
            }

            return sendMessageResult.Success
                ? Result.Ok()
                : sendMessageResult;
        }
    }
}
