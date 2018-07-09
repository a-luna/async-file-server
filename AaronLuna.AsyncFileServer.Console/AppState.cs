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
        public ProgressEventArgs FileStalledInfo { get; set; }
        public ServerInfo SelectedServerInfo { get; set; }

        public FileTransferLogLevel LogLevel { get; set; }

        public string LocalServerInfo()
        {
            var serverIsListening = LocalServer.IsListening
                ? $"Server is listening on port {LocalServer.Info.PortNumber}"
                : "Server is currently not listening for incoming connections";

            var localServerIp =
                $"Local IP:  {LocalServer.Info.LocalIpAddress}{Environment.NewLine}" +
                $"Public IP: {LocalServer.Info.PublicIpAddress}{Environment.NewLine}";

            var filePlural = LocalServer.PendingFileTransferCount > 1
                ? "requests"
                : "request";

            var fileTransferQueue = LocalServer.NoFileTransfersPending
                ? "No pending file transfers"
                : $"{LocalServer.PendingFileTransferCount} pending file transfer {filePlural}";

            var transferInProgress = LocalServer.FileTransferInProgress
                ? "FILE TRANSFER IN PROGRESS"
                : fileTransferQueue;

            var messagePlural = LocalServer.UnreadTextMessageCount > 1
                ? "messages"
                : "message";

            var unreadTextMessages = LocalServer.UnreadTextMessageCount == 0
                ? "No unread messages"
                : $"{LocalServer.UnreadTextMessageCount} unread {messagePlural}";
            
            return
                serverIsListening + Environment.NewLine +
                localServerIp + Environment.NewLine +
                transferInProgress + Environment.NewLine +
                unreadTextMessages + Environment.NewLine;
        }

        public string RemoteServerInfo()
        {
            var selectedServerStatus = RemoteServerSelected
                ? $"Selected Server: {SelectedServerInfo}"
                : "Please select a remote server";

            return FileTransferInProgress
                ? $"SENDING FILE TO {LocalServer.RemoteServerInfo}..."
                : selectedServerStatus;
        }
    }
}
