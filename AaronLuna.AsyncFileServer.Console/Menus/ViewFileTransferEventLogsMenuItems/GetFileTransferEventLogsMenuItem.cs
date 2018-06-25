namespace AaronLuna.AsyncFileServer.Console.Menus.ViewFileTransferEventLogsMenuItems
{
    using System;
    using System.Threading.Tasks;

    using Model;
    using Common.Console.Menu;
    using Common.Result;

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
            System.Console.WriteLine($"{Environment.NewLine}############ FILE TRANSFER EVENT LOG ############{Environment.NewLine}");
            foreach (var serverEvent in _fileTransfer.EventLog)
            {
                System.Console.WriteLine(serverEvent);
            }

            System.Console.WriteLine($"{Environment.NewLine}############  FILE TRANSFER DETAILS  ############{Environment.NewLine}");
            System.Console.WriteLine(_fileTransfer.TransferDetails());
            System.Console.WriteLine($"{Environment.NewLine}Press enter to return to the previous menu.");
            System.Console.ReadLine();
            
            return Result.Ok();
        }
    }
}
