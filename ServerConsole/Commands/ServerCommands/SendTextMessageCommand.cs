
namespace ServerConsole.Commands.ServerCommands
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Result;

    class SendTextMessageCommand : ICommand
    {
        readonly AppState _state;

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
            var ipAddress = _state.ClientSessionIpAddress;
            var port = _state.ClientServerPort;

            Console.Clear();
            Console.WriteLine($"Please enter a text message to send to {ipAddress}:{port}");
            var message = Console.ReadLine();

            _state.WaitingForUserInput = false;

            var sendMessageResult =
                await _state.Server.SendTextMessageAsync(
                    message,
                    ipAddress,
                    port,
                    _state.MyLocalIpAddress,
                    _state.MyServerPort,
                    new CancellationToken()).ConfigureAwait(false);

            return sendMessageResult.Success
                ? Result.Ok()
                : Result.Fail(sendMessageResult.Error);
        }
    }
}
