namespace ServerConsole
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Threading;

    using AaronLuna.ConsoleProgressBar;

    using TplSockets;

    class AppState
    {
        public AppState()
        {
            LocalServer = new TplSocketServer();
            SelectedServer = new ServerInfo();
            
            WaitingForServerInfoResponse = true;
            WaitingForFileListResponse = true;

            SignalReturnToMainMenu = new AutoResetEvent(false);
        }

        public ServerSettings Settings { get; set; }
        public FileInfo SettingsFile { get; set; }
        public string SettingsFilePath => SettingsFile.ToString();
        public int MessageDisplayTime { get; set; }
        public AutoResetEvent SignalReturnToMainMenu { get; set; }

        public bool WaitingForServerInfoResponse { get; set; }
        public bool WaitingForFileListResponse { get; set; }
        public bool ClientSelected { get; set; }
        public bool ErrorOccurred { get; set; }
        public bool ProgressBarInstantiated { get; set; }
        public bool RequestedFolderDoesNotExist { get; set; }
        public bool NoFilesAvailableForDownload { get; set; }
        public bool DoNotRefreshMainMenu { get; set; }
        public bool RestartRequired { get; set; }

        public int InboundFileTransferId { get; set; }
        public int LogViewerFileTransferId { get; set; }
        public bool FileTransferRejected => LocalServer.FileTransferRejected;
        public bool FileTransferInProgress { get; set; }
        public bool FileTransferStalled => LocalServer.FileTransferStalled;
        public bool FileTransferAccepted => LocalServer.FileTransferAccepted;
        public bool FileTransferCanceled => LocalServer.FileTransferCanceled;

        public string UserEntryLocalNetworkCidrIp { get; set; }
        public IPAddress UserEntryLocalIpAddress { get; set; }
        public IPAddress UserEntryPublicIpAddress { get; set; }
        public int UserEntryLocalServerPort { get; set; }

        public TplSocketServer LocalServer { get; set; }
        public string IncomingFileName => Path.GetFileName(LocalServer.IncomingFilePath);
        public List<(string filePath, long fileSize)> RemoteServerFileList => LocalServer.RemoteServerFileList;
        public string ErrorMessage { get; set; }

        public FileTransferProgressBar ProgressBar { get; set; }
        public int RetryCounter { get; set; }
        public ProgressEventArgs FileStalledInfo { get; set; }

        public ServerInfo SelectedServer { get; set; }

        public void DisplayCurrentStatus()
        {
            Console.Clear();
            Console.WriteLine(ReportLocalServerConnectionInfo());
            Console.WriteLine(ReportItemsInQueue());
            Console.WriteLine(ReportRemoteServerConnectionInfo());
        }

        public string ReportLocalServerConnectionInfo()
        {
            return $"Server is listening for incoming requests on port {LocalServer.Info.PortNumber}{Environment.NewLine}" +
                   $"Local IP:  {LocalServer.Info.LocalIpAddress}{Environment.NewLine}" +
                   $"Public IP: {LocalServer.Info.PublicIpAddress}{Environment.NewLine}";
        }

        public string ReportItemsInQueue()
        {
            return $"Requests in queue: {LocalServer.RequestsInQueue}{Environment.NewLine}";
        }

        public string ReportRemoteServerConnectionInfo()
        {
            var selectedServerInfo = $"{SelectedServer.SessionIpAddress}:{SelectedServer.PortNumber}";
            var remoteServerInfo = $"{LocalServer.RemoteServerSessionIpAddress}:{LocalServer.RemoteServerPortNumber}";

            var selectedServerStatus = ClientSelected
                ? $"Remote server endpoint: {selectedServerInfo}{Environment.NewLine}"
                : $"Please select a remote server{Environment.NewLine}";

            return FileTransferInProgress
                ? $"SENDING FILE TO {remoteServerInfo}...{Environment.NewLine}"
                : selectedServerStatus;
        }
    }
}
