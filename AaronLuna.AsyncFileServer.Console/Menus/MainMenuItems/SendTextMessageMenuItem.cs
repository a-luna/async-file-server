namespace AaronLuna.AsyncFileServer.Console.Menus.MainMenuItems
{
    using System;
    using System.Threading.Tasks;

    using Common.Console.Menu;
    using Common.Result;

    class SendTextMessageMenuItem : IMenuItem
    {
        readonly AppState _state;

        public SendTextMessageMenuItem(AppState state)
        {
            _state = state;

            ReturnToParent = false;
            ItemText = "Send text message to remote server";
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public async Task<Result> ExecuteAsync()
        {
            var ipAddress = _state.SelectedServer.SessionIpAddress.ToString();
            var port = _state.SelectedServer.PortNumber;

            System.Console.WriteLine($"{Environment.NewLine}Please enter a text message to send to {ipAddress}:{port}");
            var message = System.Console.ReadLine();

            var sendMessageResult =
                await _state.LocalServer.SendTextMessageAsync(
                    message,
                    ipAddress,
                    port).ConfigureAwait(false);

            return sendMessageResult.Success
                ? Result.Ok()
                : sendMessageResult;
        }
    }
}
