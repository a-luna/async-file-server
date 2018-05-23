namespace ServerConsole.Commands.Getters
{
    using System;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Result;

    using TplSockets;

    class GetEventLogsForArchivedRequestMenuItem: IMenuItem
    {
        readonly Message _message;

        public GetEventLogsForArchivedRequestMenuItem(Message message)
        {
            _message = message;

            ReturnToParent = false;
            ItemText = message.ToString();
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }
        public async Task<Result> ExecuteAsync()
        {
            Console.WriteLine();
            foreach (var serverEvent in _message.EventLog)
            {
                Console.WriteLine(serverEvent);
            }

            Console.WriteLine($"{Environment.NewLine}Press enter to return to the previous menu.");
            Console.ReadLine();

            await Task.Delay(1);
            return Result.Ok();
        }
    }
}
