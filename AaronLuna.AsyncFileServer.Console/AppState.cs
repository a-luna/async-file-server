namespace AaronLuna.AsyncFileServer.Console
{
    using System;
    using System.IO;
    using System.Net;
    using System.Threading;

    using Common.Result;

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
        }

        public ServerSettings Settings { get; set; }
        public FileInfo SettingsFile { get; set; }
        public string SettingsFilePath => SettingsFile.ToString();
        public int MessageDisplayTime { get; set; }
        public AutoResetEvent SignalReturnToMainMenu { get; set; }

        public bool WaitingForServerInfoResponse { get; set; }
        public bool WaitingForFileListResponse { get; set; }
        public bool WaitingForUserToConfirmNewRemoteServerDetails { get; set; }
        public bool RemoteServerSelected { get; set; }
        public bool ErrorOccurred { get; set; }
        public bool ProgressBarInstantiated { get; set; }
        public bool RequestedFolderDoesNotExist { get; set; }
        public bool NoFilesAvailableForDownload { get; set; }
        public bool DoNotRefreshMainMenu { get; set; }
        public bool DoNotRequestServerInfo { get; set; }
        public bool DoNotPromptUserForServerName { get; set; }
        public bool RestartRequired { get; set; }

        public int InboundFileTransferId { get; set; }
        public int LogViewerFileTransferId { get; set; }
        public int LogViewerRequestId { get; set; }
        public bool OutboundFileTransferInProgress { get; set; }
        public bool InboundFileTransferInProgress { get; set; }

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

        public string LocalServerInfo()
        {
            var serverIsListening = LocalServer.IsListening
                ? $"Server is listening on port {LocalServer.MyInfo.PortNumber}"
                : "SERVER IS NOT RUNNING";

            var localServerIp =
                $"LAN CIDR IP..: {Settings.LocalNetworkCidrIp}{Environment.NewLine}" +
                $"Local IP.....: {LocalServer.MyInfo.LocalIpAddress}{Environment.NewLine}" +
                $"Public IP....: {LocalServer.MyInfo.PublicIpAddress}{Environment.NewLine}";

            var filePlural = LocalServer.PendingFileTransferCount > 1
                ? "requests"
                : "request";

            var fileTransferQueue = LocalServer.FileTransferPending
                ? $"{LocalServer.PendingFileTransferCount} pending file transfer {filePlural}"
                : "No pending file transfers";

            var transferInProgress = InboundFileTransferInProgress || OutboundFileTransferInProgress
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
            return RemoteServerSelected
                ? $"Selected Server: {SelectedServerInfo}"
                : "Please select a remote server";
        }

        public Result SaveSettingsToFile()
        {
            return ServerSettings.SaveToFile(Settings, SettingsFilePath);
        }
    }
}
