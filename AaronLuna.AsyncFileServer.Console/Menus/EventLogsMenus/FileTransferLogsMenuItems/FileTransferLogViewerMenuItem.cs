namespace AaronLuna.AsyncFileServer.Console.Menus.EventLogsMenus.FileTransferLogsMenuItems
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    
    using Common.Console.Menu;
    using Common.Result;

    using Controller;
    using Model;

    class FileTransferLogViewerMenuItem: IMenuItem
    {
        readonly AppState _state;
        readonly List<ServerEvent> _eventLog;
        readonly FileTransferController _fileTransfer;

        public FileTransferLogViewerMenuItem(
            AppState state,
            FileTransferController fileTransferController,
            List<ServerEvent> eventLog)
        {
            _state = state;
            _fileTransfer = fileTransferController;
            _eventLog = eventLog;

            ReturnToParent = false;
            ItemText = _fileTransfer.ToString();
        }

        public string ItemText { get; set; }
        public bool ReturnToParent { get; set; }

        public Task<Result> ExecuteAsync()
        {
            return Task.Run((Func<Result>) Execute);
        }

        Result Execute()
        {
            SharedFunctions.DisplayLocalServerInfo(_state);
            Console.WriteLine($"############ FILE TRANSFER EVENT LOG ############{Environment.NewLine}");
            foreach (var serverEvent in _eventLog)
            {
                Console.WriteLine(serverEvent.ToString());
            }

            Console.WriteLine($"{Environment.NewLine}############  FILE TRANSFER DETAILS  ############{Environment.NewLine}");
            Console.WriteLine(_fileTransfer.TransferDetails());
            Console.WriteLine(Environment.NewLine + Resources.Prompt_ReturnToPreviousMenu);
            Console.ReadLine();

            return Result.Ok();
        }
    }
}
