namespace ServerConsole.Menus.ViewFileTransferEventLogsMenuItems
{
    using System;
    using System.Threading.Tasks;

    using AaronLuna.Common.Console.Menu;
    using AaronLuna.Common.Result;

    using TplSockets;

    class GetFileTransferEventLogsMenuItem: IMenuItem
    {
        readonly FileTransfer _fileTransfer;

        public GetFileTransferEventLogsMenuItem(FileTransfer fileTransfer)
        {
            _fileTransfer = fileTransfer;

            ReturnToParent = false;
            ItemText = fileTransfer.ToString();
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }
        public Task<Result> ExecuteAsync()
        {
            return Task.Run((Func<Result>) Execute);
        }

        Result Execute()
        {
            Console.WriteLine();
            foreach (var serverEvent in _fileTransfer.EventLog)
            {
                Console.WriteLine(serverEvent);
            }

            Console.WriteLine($"{Environment.NewLine}Press enter to return to the previous menu.");
            Console.ReadLine();
            
            return Result.Ok();
        }
    }
}
