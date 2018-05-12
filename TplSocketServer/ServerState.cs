namespace TplSockets
{
    using System.Collections.Generic;
    using System.IO;

    internal class ServerState
    {
        FileInfo _outgoingFile;
        FileInfo _incomingFile;
        FileInfo _remoteFilePath;

        public ServerState()
        {
            UnreadBytes = new List<byte>();

            _outgoingFile = null;
            _incomingFile = null;
            _remoteFilePath = null;
        }
        
        public byte[] Buffer { get; set; }
        public List<byte> UnreadBytes { get; set; }
        public int LastBytesReceivedCount { get; set; }
        public int LastBytesSentCount { get; set; }
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
    }
}
