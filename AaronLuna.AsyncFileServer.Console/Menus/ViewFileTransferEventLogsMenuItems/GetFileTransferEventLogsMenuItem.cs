using System.Collections.Generic;

namespace AaronLuna.AsyncFileServer.Console.Menus.ViewFileTransferEventLogsMenuItems
{
    using System;
    using System.Threading.Tasks;

    using Model;
    using Common.Console.Menu;
    using Common.Result;

    class GetFileTransferEventLogsMenuItem: IMenuItem
    {
        readonly List<ServerEvent> _eventLog;
        readonly FileTransfer _fileTransfer;

        public GetFileTransferEventLogsMenuItem(FileTransfer fileTransfer, List<ServerEvent> eventLog)
        {
            _fileTransfer = fileTransfer;
            _eventLog = eventLog;

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
            Console.WriteLine($"{Environment.NewLine}############ FILE TRANSFER EVENT LOG ############{Environment.NewLine}");
            foreach (var serverEvent in _eventLog)
            {
                Console.WriteLine(serverEvent);
            }

            Console.WriteLine($"{Environment.NewLine}############  FILE TRANSFER DETAILS  ############{Environment.NewLine}");
            Console.WriteLine(_fileTransfer.TransferDetails());
            Console.WriteLine($"{Environment.NewLine}Press enter to return to the previous menu.");
            Console.ReadLine();
            
            return Result.Ok();
        }
    }
}
