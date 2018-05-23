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

            WaitingForServerToBeginAcceptingConnections = true;
            WaitingForTransferFolderResponse = true;
            WaitingForPublicIpResponse = true;
            WaitingForFileListResponse = true;
            WaitingForDownloadToComplete = true;

            SignalRetryLimitExceeded = new AutoResetEvent(true);
            SignalReturnToMainMenu = new AutoResetEvent(true);
        }

        public ServerSettings Settings { get; set; }
        public FileInfo SettingsFile { get; set; }
        public string SettingsFilePath => SettingsFile.ToString();

        public bool WaitingForServerToBeginAcceptingConnections { get; set; }
        public bool WaitingForTransferFolderResponse { get; set; }
        public bool WaitingForPublicIpResponse { get; set; }
        public bool WaitingForFileListResponse { get; set; }
        public bool WaitingForDownloadToComplete { get; set; }
        public bool ClientSelected { get; set; }
        public bool ErrorOccurred { get; set; }
        public bool ProgressBarInstantiated { get; set; }
        public bool FileTransferRejected { get; set; }
        public bool RequestedFolderDoesNotExist { get; set; }
        public bool NoFilesAvailableForDownload { get; set; }
        public bool FileTransferStalled { get; set; }
        public bool FileTransferCanceled { get; set; }
        public bool DoNotRefreshMainMenu { get; set; }
        public bool RestartRequired { get; set; }
        public bool ShutdownInitiated { get; set; }

        public string UserEntryLocalNetworkCidrIp { get; set; }
        public IPAddress UserEntryLocalIpAddress { get; set; }
        public IPAddress UserEntryPublicIpAddress { get; set; }
        public int UserEntryLocalServerPort { get; set; }

        public TplSocketServer LocalServer { get; set; }
        public string IncomingFileName => Path.GetFileName(LocalServer.IncomingFilePath);
        public List<(string filePath, long fileSize)> FileInfoList => LocalServer.RemoteServerFileList;

        public FileTransferProgressBar ProgressBar { get; set; }
        public int RetryCounter { get; set; }
        public ProgressEventArgs FileStalledInfo { get; set; }
        public AutoResetEvent SignalRetryLimitExceeded { get; set; }
        public AutoResetEvent SignalReturnToMainMenu { get; set; }

        public ServerInfo SelectedServer { get; set; }

        public ServerInfo RemoteServerInfo
        {
            get => LocalServer.RemoteServerInfo;
            set => LocalServer.RemoteServerInfo = value;
        }

        public void DisplayCurrentStatus()
        {
            Console.Clear();
            Console.WriteLine(ReportLocalServerConnectionInfo());
            Console.WriteLine(ReportItemsInQueue());
            Console.WriteLine(ReportRemoteServerConnectionInfo());
        }

        public string ReportLocalServerConnectionInfo()
        {
            return $"Server is listening for incoming requests on port {LocalServer.Info.Port}{Environment.NewLine}" +
                   $"Local IP:  {LocalServer.Info.LocalIpAddress}{Environment.NewLine}" +
                   $"Public IP: {LocalServer.Info.PublicIpAddress}{Environment.NewLine}";
        }

        public string ReportItemsInQueue()
        {
            return $"Requests in queue: {LocalServer.Queue.Count}{Environment.NewLine}" +
                   $"Total requests processed: {LocalServer.Archive.Count}{Environment.NewLine}";
        }

        public string ReportRemoteServerConnectionInfo()
        {
            return ClientSelected
                ? $"Remote server endpoint: {RemoteServerInfo.SessionIpAddress}:{RemoteServerInfo.Port}{Environment.NewLine}"
                : $"Please select a remote server{Environment.NewLine}";
            ;
        }
    }
}
