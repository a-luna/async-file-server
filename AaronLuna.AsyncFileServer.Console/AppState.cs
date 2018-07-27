namespace AaronLuna.AsyncFileServer.Console
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Threading;

    using Common.Result;

    using Controller;
    using Model;
    using ConsoleProgressBar;

    class AppState
    {
        readonly ServerState _localServerState;

        public AppState()
        {
            LocalServer = new AsyncFileServer();            
            SelectedServerInfo = new ServerInfo();
            RemoteServerFileList = new FileInfoList();

            WaitingForServerInfoResponse = true;
            WaitingForFileListResponse = true;

            SignalReturnToMainMenu = new AutoResetEvent(false);

            UserEntryRemoteServerName = string.Empty;
            UserEntryIpAddress = IPAddress.None;
            UserEntryPortNumber = 0;

            _localServerState = new ServerState(LocalServer);
        }

        public ServerSettings Settings { get; set; }
        public FileInfo SettingsFile { get; set; }
        public string SettingsFilePath => SettingsFile.ToString();

        public AsyncFileServer LocalServer { get; set; }        
        public ServerInfo SelectedServerInfo { get; set; }
        public FileInfoList RemoteServerFileList { get; set; }

        public int InboundFileTransferId { get; set; }
        public int LogViewerFileTransferId { get; set; }
        public DateTime LogViewerRequestBoundary { get; set; }
        public bool OutboundFileTransferInProgress { get; set; }
        public bool InboundFileTransferInProgress { get; set; }

        public bool WaitingForServerInfoResponse { get; set; }
        public bool WaitingForFileListResponse { get; set; }
        public bool WaitingForUserToConfirmServerDetails { get; set; }
        public bool RemoteServerSelected { get; set; }
        public bool ProgressBarInstantiated { get; set; }
        public bool RequestedFolderDoesNotExist { get; set; }
        public bool NoFilesAvailableForDownload { get; set; }
        public bool DoNotRefreshMainMenu { get; set; }
        public bool DoNotRequestServerInfo { get; set; }
        public bool PromptUserForServerName { get; set; }
        public bool RestartRequired { get; set; }

        public string UserEntryLocalNetworkCidrIp { get; set; }
        public IPAddress UserEntryIpAddress { get; set; }
        public IPAddress UserEntryPublicIpAddress { get; set; }
        public int UserEntryPortNumber { get; set; }
        public string UserEntryRemoteServerName { get; set; }

        public AutoResetEvent SignalReturnToMainMenu { get; set; }
        public FileTransferProgressBar ProgressBar { get; set; }
        public ProgressEventArgs FileStalledInfo { get; set; }

        public bool NoFileTransfersPending => _localServerState.NoFileTransfersPending;
        public bool FileTransferPending => _localServerState.FileTransferPending;
        public int PendingFileTransferCount => _localServerState.PendingFileTransferCount;
        public List<int> PendingFileTransferIds => _localServerState.PendingFileTransferIds;

        public bool NoRequests => _localServerState.NoRequests;
        public List<int> RequestIds => _localServerState.RequestIds;
        public DateTime MostRecentRequestTime => _localServerState.MostRecentRequestTime;

        public bool AllErrorsHaveBeenRead => _localServerState.AllErrorsHaveBeenRead;
        public List<ServerError> UnreadErrors => _localServerState.UnreadErrors;

        public bool NoTextSessions => _localServerState.NoTextSessions;
        public List<int> TextSessionIds => _localServerState.TextSessionIds;
        public int UnreadTextMessageCount => _localServerState.UnreadTextMessageCount;
        public List<int> TextSessionIdsWithUnreadMessages => _localServerState.TextSessionIdsWithUnreadMessages;

        public bool NoFileTransfers => _localServerState.NoFileTransfers;
        public List<int> FileTransferIds => _localServerState.FileTransferIds;
        public int MostRecentFileTransferId => _localServerState.MostRecentFileTransferId;
        public List<int> StalledFileTransferIds => _localServerState.StalledFileTransferIds;

        public string LocalServerInfo()
        {
            var serverIsListening = LocalServer.IsListening
                ? $"Server is listening on port {LocalServer.MyInfo.PortNumber}"
                : "SERVER IS NOT RUNNING";

            var localServerIp =
                $"LAN CIDR IP..: {Settings.LocalNetworkCidrIp}{Environment.NewLine}" +
                $"Local IP.....: {LocalServer.MyInfo.LocalIpAddress}{Environment.NewLine}" +
                $"Public IP....: {LocalServer.MyInfo.PublicIpAddress}{Environment.NewLine}";

            var filePlural = _localServerState.PendingFileTransferCount > 1
                ? "requests"
                : "request";

            var fileTransferQueue = _localServerState.FileTransferPending
                ? $"{_localServerState.PendingFileTransferCount} pending file transfer {filePlural}"
                : "No pending file transfers";

            var transferInProgress = InboundFileTransferInProgress || OutboundFileTransferInProgress
                ? "FILE TRANSFER IN PROGRESS"
                : fileTransferQueue;

            var messagePlural = UnreadTextMessageCount > 1
                ? "messages"
                : "message";

            var unreadTextMessages = UnreadTextMessageCount == 0
                ? "No unread messages"
                : $"{UnreadTextMessageCount} unread {messagePlural}";

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
