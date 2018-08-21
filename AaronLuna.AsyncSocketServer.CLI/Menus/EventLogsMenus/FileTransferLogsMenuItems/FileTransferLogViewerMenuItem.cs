using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AaronLuna.AsyncSocketServer.FileTransfers;
using AaronLuna.Common.Console.Menu;
using AaronLuna.Common.Result;

namespace AaronLuna.AsyncSocketServer.CLI.Menus.EventLogsMenus.FileTransferLogsMenuItems
{
    class FileTransferLogViewerMenuItem: IMenuItem
    {
        readonly AppState _state;
        readonly List<ServerEvent> _eventLog;
        readonly FileTransfer _fileTransfer;

        public FileTransferLogViewerMenuItem(
            AppState state,
            FileTransfer fileTransferController,
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
