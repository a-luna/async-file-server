namespace AaronLuna.AsyncFileServer.Console.Menus.ViewLogsMenus.ViewFileTransferEventLogsMenuItems
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
        readonly List<ServerEvent> _eventLog;
        readonly FileTransfer _fileTransfer;
        readonly FileTransferLogLevel _logLevel;

        public FileTransferLogViewerMenuItem(
            FileTransferController fileTransferController,
            List<ServerEvent> eventLog,
            FileTransferLogLevel logLevel)
        {
            _fileTransfer = fileTransferController.FileTransfer;
            _eventLog = eventLog;
            _logLevel = logLevel;

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
            Console.WriteLine($"{Environment.NewLine}############ FILE TRANSFER EVENT LOG ############{Environment.NewLine}");
            foreach (var serverEvent in _eventLog)
            {
                Console.WriteLine(GetServerEventDetailsforLogLevel(serverEvent));
            }

            Console.WriteLine($"{Environment.NewLine}############  FILE TRANSFER DETAILS  ############{Environment.NewLine}");
            Console.WriteLine(_fileTransfer.TransferDetails());
            Console.WriteLine($"{Environment.NewLine}Press enter to return to the previous menu.");
            Console.ReadLine();

            return Result.Ok();
        }

        string GetServerEventDetailsforLogLevel(ServerEvent serverEvent)
        {
            switch (_logLevel)
            {
                case FileTransferLogLevel.Debug:
                    return $"{DateTime.Now:MM/dd/yyyy HH:mm:ss.fff}\t{serverEvent}";

                default:
                    return serverEvent.ToString();
            }
        }
    }
}
