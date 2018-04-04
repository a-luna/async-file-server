namespace TplSockets
{
    using System.Collections.Generic;
    using System.IO;
    using System.Net;

    public class ServerState
    {
        FileInfo _outgoingFile;
        FileInfo _incomingFile;
        FileInfo _remoteFilePath;

        public ServerState(IPAddress localIpAddress, int port)
        {
            SocketSettings = new SocketSettings
            {
                MaxNumberOfConnections = 5,
                BufferSize = 1024,
                ConnectTimeoutMs = 5000,
                ReceiveTimeoutMs = 5000,
                SendTimeoutMs = 5000
            };
            
            UnreadBytes = new List<byte>();
            LastAcceptedConnectionIp = IPAddress.None;

            MyTransferFolderPath = GetDefaultTransferFolder();
            MyInfo = new ConnectionInfo(localIpAddress, port);
            ClientInfo = new ConnectionInfo();

            _outgoingFile = null;
            _incomingFile = null;
            _remoteFilePath = null;
        }

        public SocketSettings SocketSettings { get; set; }
        public IPAddress LastAcceptedConnectionIp { get; set; }
        public ConnectionInfo MyInfo { get; set; }
        public ConnectionInfo ClientInfo { get; set; }
        public IPEndPoint TextMessageEndPoint { get; set; }
        public byte[] Buffer { get; set; }
        public List<byte> UnreadBytes { get; set; }
        public int LastBytesReceivedCount { get; set; }
        public int LastBytesSentCount { get; set; }
        public string MyTransferFolderPath { get; set; }
        public string ClientTransferFolderPath { get; set; }
        public float TransferUpdateInterval { get; set; }
        public bool FileTransferStalled { get; set; }
        public bool FileTransferCanceled { get; set; }
        public List<(string filePath, long fileSize)> FileListInfo { get; set; }

        public string OutgoingFilePath
        {
            get => _outgoingFile.ToString();
            set => _outgoingFile = new FileInfo(value);
        }

        public long OutgoingFileSize => _outgoingFile.Length;

        public string IncomingFilePath
        {
            get => _incomingFile.ToString();
            set => _incomingFile = new FileInfo(value);
        }

        public long IncomingFileSize { get; set; }

        public string RemoteFilePath
        {
            get => _remoteFilePath.ToString();
            set => _remoteFilePath = new FileInfo(value);
        }
        
        public int MaxNumberOfConnections => SocketSettings.MaxNumberOfConnections;
        public int BufferSize => SocketSettings.BufferSize;
        public int ConnectTimeoutMs => SocketSettings.ConnectTimeoutMs;
        public int ReceiveTimeoutMs => SocketSettings.ReceiveTimeoutMs;
        public int SendTimeoutMs => SocketSettings.SendTimeoutMs;

        public IPEndPoint MyLocalIpEndPoint => new IPEndPoint(MyLocalIpAddress, MyServerPort);
        public IPEndPoint MyPublicEndPoint => new IPEndPoint(MyPublicIpAddress, MyServerPort);
        public IPAddress MyLocalIpAddress => MyInfo.LocalIpAddress;
        public IPAddress MyPublicIpAddress => MyInfo.PublicIpAddress;
        public int MyServerPort => MyInfo.Port;

        public IPEndPoint ClientEndPoint => new IPEndPoint(ClientSessionIpAddress, ClientServerPort);
        public IPAddress ClientSessionIpAddress => ClientInfo.SessionIpAddress;
        public IPAddress ClientLocalIpAddress => ClientInfo.LocalIpAddress;
        public IPAddress ClientPublicIpAddress => ClientInfo.PublicIpAddress;
        public int ClientServerPort => ClientInfo.Port;

        static string GetDefaultTransferFolder()
        {
            var defaultPath = $"{Directory.GetCurrentDirectory()}{Path.DirectorySeparatorChar}transfer";

            if (!Directory.Exists(defaultPath))
            {
                Directory.CreateDirectory(defaultPath);
            }

            return defaultPath;
        }
    }
}
