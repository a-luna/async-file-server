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

        public ConnectionInfo MyInfo => Server.MyInfo;
        
        public string MyTransferFolderPath
        {
            get => Server.MyTransferFolderPath;
            set => Server.MyTransferFolderPath = value;
        }

        public IPAddress MyLocalIpAddress => Server.MyLocalIpAddress;
        public IPAddress MyPublicIpAddress => Server.MyPublicIpAddress;
        public int MyServerPort => Server.MyServerPort;

        public ConnectionInfo ClientInfo
        {
            get => Server.ClientInfo;
            set => Server.ClientInfo = value;
        }

        public string ClientTransferFolderPath
        {
            get => Server.ClientTransferFolderPath;
            set => Server.ClientTransferFolderPath = value;
        }

        public IPAddress ClientSessionIpAddress => Server.ClientSessionIpAddress;
        public IPAddress ClientLocalIpAddress => Server.ClientLocalIpAddress;
        public IPAddress ClientPublicIpAddress => Server.ClientPublicIpAddress;
        public int ClientServerPort => Server.ClientServerPort;
    }
}
