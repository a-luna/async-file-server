
namespace TplSocketServer
{
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Sockets;

    public class ServerState
    {
        public ServerState()
        {
            SocketSettings = new SocketSettings();
            UnreadBytes = new List<byte>();
        }

        public SocketSettings SocketSettings { get; set; }
        public IPEndPoint MyEndPoint { get; set; }
        public IPEndPoint  ClientEndPoint { get; set; }
        public Socket ListenSocket { get; set; }
        public Socket ClientSocket { get; set; }
        public Socket ServerSocket { get; set; }
        public DirectoryInfo LocalFolder { get; set; }
        public DirectoryInfo RemoteFolder { get; set; }
        public FileInfo OutgoingFile { get; set; }
        public byte[] Buffer { get; set; }
        public List<byte> UnreadBytes { get; set; }
        public int LastBytesReceivedCount { get; set; }
        public int LastBytesSentCount { get; set; }
        public float TransferUpdateInterval { get; set; }
        public bool FileTransferStalled { get; set; }
        public bool FileTransferCanceled { get; set; }
        public bool ShutdownServer { get; set; }
        public bool LoggingEnabled { get; set; }

        public int MaxNumberOfConnections => SocketSettings.MaxNumberOfConections;
        public int BufferSize => SocketSettings.BufferSize;
        public int ConnectTimeout => SocketSettings.ConnectTimeoutMs;
        public int ReceiveTimeout => SocketSettings.ReceiveTimeoutMs;
        public int SendTimeout => SocketSettings.SendTimeoutMs;

        public string LocalIpAddress => MyEndPoint.Address.ToString();
        public int LocalPort => MyEndPoint.Port;
        public string OutgoingFilePath => OutgoingFile.ToString();
        public string LocalFolderPath => LocalFolder.ToString();
        public string RemoteFolderPath => RemoteFolder.ToString();
    }
}
