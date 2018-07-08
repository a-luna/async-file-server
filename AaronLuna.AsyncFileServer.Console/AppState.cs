namespace AaronLuna.AsyncFileServer.Console
{
    using System;
    using System.IO;
    using System.Net;
    using System.Threading;

    using Controller;
    using Model;
    using ConsoleProgressBar;

    class AppState
    {
        public AppState()
        {
            LocalServer = new AsyncFileServer();
            SelectedServerInfo = new ServerInfo();
            
            WaitingForServerInfoResponse = true;
            WaitingForFileListResponse = true;

            SignalReturnToMainMenu = new AutoResetEvent(false);

            UserEntryRemoteServerName = string.Empty;
            UserEntryIpAddress = IPAddress.None;
            UserEntryPortNumber = 0;

            LogLevel = FileTransferLogLevel.Normal;
        }

        public ServerSettings Settings { get; set; }
        public FileInfo SettingsFile { get; set; }
        public string SettingsFilePath => SettingsFile.ToString();
        public int MessageDisplayTime { get; set; }
        public AutoResetEvent SignalReturnToMainMenu { get; set; }

        public bool WaitingForServerInfoResponse { get; set; }
        public bool WaitingForFileListResponse { get; set; }
        public bool RemoteServerSelected { get; set; }
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
        public IPAddress UserEntryIpAddress { get; set; }
        public IPAddress UserEntryPublicIpAddress { get; set; }
        public int UserEntryPortNumber { get; set; }
        public string UserEntryRemoteServerName { get; set; }

        public AsyncFileServer LocalServer { get; set; }
        public FileInfoList RemoteServerFileList => LocalServer.RemoteServerFileList;
        public string ErrorMessage { get; set; }

        public FileTransferProgressBar ProgressBar { get; set; }
        public int RetryCounter { get; set; }
        public ProgressEventArgs FileStalledInfo { get; set; }

        public ServerInfo SelectedServerInfo { get; set; }

        public FileTransferLogLevel LogLevel { get; set; }

        public string LocalServerInfo()
        {
            var serverIsListening = LocalServer.IsListening
                ? $"Server is listening for incoming requests on port {LocalServer.Info.PortNumber}{Environment.NewLine}"
                : $"Server is currently not listening for incoming connections{Environment.NewLine}";

            var localServerIp =
                $"Local IP:  {LocalServer.Info.LocalIpAddress}{Environment.NewLine}" +
                $"Public IP: {LocalServer.Info.PublicIpAddress}{Environment.NewLine}{Environment.NewLine}";

            var filePlural = LocalServer.PendingFileTransferCount > 1
                ? "transfers"
                : "transfer";

            var fileTransferQueue = LocalServer.NoFileTransfersPending
                ? $"No pending file transfers{Environment.NewLine}"
                : $"{LocalServer.PendingFileTransferCount} file {filePlural} in queue{Environment.NewLine}";

            var transferInProgress = LocalServer.FileTransferInProgress
                ? $"FILE TRANSFER IN PROGRESS{Environment.NewLine}"
                : fileTransferQueue;

            var messagePlural = LocalServer.UnreadTextMessageCount > 1
                ? "messages"
                : "message";

            var unreadTextMessages = LocalServer.UnreadTextMessageCount == 0
                ? $"No unread text messages{Environment.NewLine}"
                : $"{LocalServer.UnreadTextMessageCount} unread text {messagePlural}{Environment.NewLine}";
            
            return
                serverIsListening + localServerIp + transferInProgress + unreadTextMessages;
        }

        public string RemoteServerInfo()
        {
            var selectedServerStatus = RemoteServerSelected
                ? $"Selected Server: {SelectedServerInfo.Name} ({SelectedServerInfo})"
                : "Please select a remote server";

            return FileTransferInProgress
                ? $"SENDING FILE TO {LocalServer.RemoteServerInfo.Name}..."
                : selectedServerStatus;
        }
    }
}
