namespace ServerConsole
{
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Threading;

    using AaronLuna.Common.Console;

    using TplSockets;

    class AppState
    {
        public AppState()
        {
            WaitingForServerToBeginAcceptingConnections = true;
            WaitingForTransferFolderResponse = true;
            WaitingForPublicIpResponse = true;
            WaitingForFileListResponse = true;
            WaitingForDownloadToComplete = true;
            WaitingForConfirmationMessage = true;
            
            UnknownHosts = new List<RemoteServer>();
            FileInfoList = new List<(string filePath, long fileSize)>();
            SignalDispayMenu = new AutoResetEvent(false);
        }

        public AutoResetEvent SignalDispayMenu { get; set; }
        public AutoResetEvent SignalExitRetryDownloadLogic { get; set; }
        public FileInfo SettingsFile { get; set; }
        public string SettingsFilePath => SettingsFile.ToString();

        public bool WaitingForServerToBeginAcceptingConnections { get; set; }
        public bool WaitingForTransferFolderResponse { get; set; }
        public bool WaitingForPublicIpResponse { get; set; }
        public bool WaitingForFileListResponse { get; set; }
        public bool WaitingForDownloadToComplete { get; set; }
        public bool WaitingForConfirmationMessage { get; set; }
        public bool WaitingForUserInput { get; set; }
        //public bool IgnoreIncomingConnections { get; set; }
        public bool ClientSelected { get; set; }
        public bool ActiveTextSession { get; set; }
        public bool ErrorOccurred { get; set; }
        public bool ClientResponseIsStalled { get; set; }
        public bool ProgressBarInstantiated { get; set; }
        public bool FileTransferRejected { get; set; }
        public bool NoFilesAvailableForDownload { get; set; }
        public bool FileTransferStalled { get; set; }
        public bool FileTransferCanceled { get; set; }

        public TplSocketServer Server { get; set; }
        public AppSettings Settings { get; set; }        
        public List<RemoteServer> UnknownHosts { get; set; }
        public IPEndPoint TextMessageEndPoint { get; set; }

        public string DownloadFileName { get; set; }
        public int RetryCounter { get; set; }
        public ProgressEventArgs FileStalledInfo { get; set; }
        public List<(string filePath, long fileSize)> FileInfoList { get; set; }
        public ConsoleProgressBar Progress { get; set; }

        public ConnectionInfo MyInfo
        {
            get => Server.State.MyInfo;
            set => Server.State.MyInfo = value;
        }
        
        public ConnectionInfo ClientInfo
        {
            get => Server.State.ClientInfo;
            set => Server.State.ClientInfo = value;
        }

        public string MyTransferFolderPath
        {
            get => Server.TransferFolderPath;
            set => Server.TransferFolderPath = value;
        }

        public string MyLocalIpAddress => Server.State.MyLocalIpAddress;
        public string MyPublicIpAddress => Server.State.MyPublicIpAddress;
        public int MyServerPort => Server.State.MyServerPort;

        public string ClientTransferFolderPath
        {
            get => Server.State.ClientTransferFolderPath;
            set => Server.State.ClientTransferFolderPath = value;
        }
        public string ClientSessionIpAddress => Server.State.ClientSessionIpAddress;
        public string ClientLocalIpAddress => Server.State.ClientLocalIpAddress;
        public string ClientPublicIpAddress => Server.State.ClientPublicIpAddress;
        public int ClientServerPort => Server.State.ClientServerPort;
    }
}
