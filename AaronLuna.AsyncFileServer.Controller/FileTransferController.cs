using System.Collections.Generic;
using AaronLuna.AsyncFileServer.Model;
using AaronLuna.Common.Logging;

namespace AaronLuna.AsyncFileServer.Controller
{
    public class FileTransferController
    {
        readonly Logger _log = new Logger(typeof(FileTransferController));

        public FileTransferController(FileTransfer fileTransfer)
        {
            FileTransfer = fileTransfer;
        }

        public FileTransfer FileTransfer { get; set; }
        public int FiletransferId => FileTransfer.Id;
        public long TransferResponseCode => FileTransfer.TransferResponseCode;
        public bool TransferStalled => FileTransfer.Status == FileTransferStatus.Stalled;
        public List<ServerEvent> EventLog => FileTransfer.EventLog;
    }
}
