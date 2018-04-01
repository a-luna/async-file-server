namespace TplSockets
{
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Sockets;

    public class ServerState
    {
        FileInfo _outgoingFile;
        FileInfo _incomingFile;
        FileInfo _remoteFilePath;

        public ServerState(IPAddress localIpAddress, int port)
        {
            SocketSettings = new SocketSettings
            {
                MaxNumberOfConections = 5,
                BufferSize = 1024,
                ConnectTimeoutMs = 5000,
                ReceiveTimeoutMs = 5000,
                SendTimeoutMs = 5000
            };

            LoggingEnabled = false;
            UnreadBytes = new List<byte>();
            LastAcceptedConnectionIp = IPAddress.None;

            MyTransferFolderPath = GetDefaultTransferFolder();
            MyInfo = new ConnectionInfo(localIpAddress, port);
            ClientInfo = new ConnectionInfo();

            ListenSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            ClientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            ServerSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            _outgoingFile = null;
            _incomingFile = null;
            _remoteFilePath = null;
        }

        public SocketSettings SocketSettings { get; set; }
        public IPAddress LastAcceptedConnectionIp { get; set; }
        public ConnectionInfo MyInfo { get; set; }
        public ConnectionInfo ClientInfo { get; set; }
        public Socket ListenSocket { get; set; }
        public Socket ClientSocket { get; set; }
        public Socket ServerSocket { get; set; }
        public byte[] Buffer { get; set; }
        public List<byte> UnreadBytes { get; set; }
        public int LastBytesReceivedCount { get; set; }
        public int LastBytesSentCount { get; set; }
        public bool LoggingEnabled { get; set; }
        public string MyTransferFolderPath { get; set; }
        public string ClientTransferFolderPath { get; set; }
        public float TransferUpdateInterval { get; set; }
        public bool FileTransferStalled { get; set; }
        public bool FileTransferCanceled { get; set; }

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

        public int MaxNumberOfConnections => SocketSettings.MaxNumberOfConections;
        public int BufferSize => SocketSettings.BufferSize;
        public int ConnectTimeout => SocketSettings.ConnectTimeoutMs;
        public int ReceiveTimeout => SocketSettings.ReceiveTimeoutMs;
        public int SendTimeout => SocketSettings.SendTimeoutMs;

        public string MyLocalIpAddress => MyInfo.LocalIpAddress.ToString();
        public string MyPublicIpAddress => MyInfo.PublicIpAddress.ToString();
        public int MyServerPort => MyInfo.Port;

        public string ClientSessionIpAddress => ClientInfo.SessionIpAddress.ToString();
        public string ClientLocalIpAddress => ClientInfo.LocalIpAddress.ToString();
        public string ClientPublicIpAddress => ClientInfo.PublicIpAddress.ToString();
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
