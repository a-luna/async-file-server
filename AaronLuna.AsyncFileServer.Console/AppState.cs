namespace AaronLuna.AsyncFileServer.Console
{
    using System;
    using System.IO;
    using System.Net;
    using System.Threading;

    using Model;
    using ConsoleProgressBar;

    class AppState
    {
        public AppState()
        {
            LocalServer = new Controller.AsyncFileServer();
            SelectedServerInfo = new ServerInfo();
            
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

        public Controller.AsyncFileServer LocalServer { get; set; }
        public FileInfoList RemoteServerFileList => LocalServer.RemoteServerFileList;
        public string ErrorMessage { get; set; }

        public FileTransferProgressBar ProgressBar { get; set; }
        public int RetryCounter { get; set; }
        public ProgressEventArgs FileStalledInfo { get; set; }

        public ServerInfo SelectedServerInfo { get; set; }

        public string LocalServerInfo()
        {
            var serverIsListening = LocalServer.IsListening
                ? $"Server is listening for incoming requests on port {LocalServer.Info.PortNumber}{Environment.NewLine}"
                : $"Server is currently not listening for incoming connections{Environment.NewLine}";

            var localServerIp =
                $"Local IP:  {LocalServer.Info.LocalIpAddress}{Environment.NewLine}" +
                $"Public IP: {LocalServer.Info.PublicIpAddress}";

            var newLine1 = LocalServer.NoTextSessions && LocalServer.NoFileTransfers
                ? Environment.NewLine
                : Environment.NewLine + Environment.NewLine;

            var textSessions = LocalServer.NoTextSessions
                ? string.Empty
                : $"Unread text messages: {LocalServer.UnreadTextMessageCount}{Environment.NewLine}";

            var newLine2 = string.IsNullOrEmpty(newLine1) && !LocalServer.NoTextSessions
                ? Environment.NewLine
                : string.Empty;

            var fileTransferQueue = LocalServer.QueueIsEmpty
                ? string.Empty
                : $"File transfers in queue: {LocalServer.RequestsInQueue}";

            var newLine3 = string.IsNullOrEmpty(newLine2) && !LocalServer.NoFileTransfers
                ? Environment.NewLine
                : string.Empty;

            return
                serverIsListening + localServerIp + newLine1
                + textSessions + newLine2
                + fileTransferQueue + newLine3;
        }

        public string RemoteServerInfo()
        {
            var selectedServerStatus = ClientSelected
                ? $"Options for remote server: {SelectedServerInfo}"
                : "Please select a remote server";

            return FileTransferInProgress
                ? $"SENDING FILE TO {LocalServer.RemoteServerInfo}..."
                : selectedServerStatus;
        }
    }
}
