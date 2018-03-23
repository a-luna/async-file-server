using System.Collections.Generic;
using System.IO;
using System.Net;
using AaronLuna.Common.Console;

namespace ServerConsole
{
    using TplSocketServer;

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
            
            MyInfo = new ConnectionInfo();
            ClientInfo = new ConnectionInfo();
            FileInfoList = new List<(string filePath, long fileSize)>();
        }

        public FileInfo SettingsFile { get; set; }

        public bool WaitingForServerToBeginAcceptingConnections { get; set; }
        public bool WaitingForTransferFolderResponse { get; set; }
        public bool WaitingForPublicIpResponse { get; set; }
        public bool WaitingForFileListResponse { get; set; }
        public bool WaitingForDownloadToComplete { get; set; }
        public bool WaitingForConfirmationMessage { get; set; }
        public bool WaitingForUserInput { get; set; }
        public bool ServerIsListening { get; set; }
        public bool ClientSelected { get; set; }
        public bool ActiveTextSession { get; set; }
        public bool ErrorOccurred { get; set; }
        public bool ClientResponseIsStalled { get; set; }
        public bool FileTransferRejected { get; set; }
        public bool NoFilesAvailableForDownload { get; set; }
        public bool FileTransferStalled { get; set; }
        public bool FileTransferCanceled { get; set; }

        public TplSocketServer Server { get; set; }
        public AppSettings Settings { get; set; }
        public ConnectionInfo MyInfo { get; set; }
        public ConnectionInfo ClientInfo { get; set; }
        public IPEndPoint TextMessageEndPoint { get; set; }

        public string DownloadFileName { get; set; }
        public int RetryCounter { get; set; }
        public ProgressEventArgs FileStalledInfo { get; set; }
        public List<(string filePath, long fileSize)> FileInfoList { get; set; }

        public string SettingsFilePath => SettingsFile.ToString();

        public string MyTransferFolderPath => Server.TransferFolderPath;
        public string MyLocalIpAddress => MyInfo.LocalIpAddress.ToString();
        public string MyPublicIpAddress => MyInfo.PublicIpAddress.ToString();
        public int MyServerPort => MyInfo.Port;

        public string ClientTransferFolderPath { get; set; }
        public string ClientSessionIpAddress => ClientInfo.SessionIpAddress.ToString();
        public string ClientLocalIpAddress => ClientInfo.LocalIpAddress.ToString();
        public string ClientPublicIpAddress => ClientInfo.PublicIpAddress.ToString();
        public int ClientServerPort => ClientInfo.Port;
    }
}
