namespace ServerConsole.Menus.ViewEventLogsMenuItems
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
        public Task<Result> ExecuteAsync()
        {
            return Task.Factory.StartNew(Execute);
        }

        Result Execute()
        {
            Console.WriteLine();
            foreach (var serverEvent in _message.EventLog)
            {
                Console.WriteLine(serverEvent);
            }

            Console.WriteLine($"{Environment.NewLine}Press enter to return to the previous menu.");
            Console.ReadLine();
            
            return Result.Ok();
        }
    }
}
