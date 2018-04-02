namespace TplSockets
{
    public class SocketSettings
    {
        public SocketSettings()
        {
            MaxNumberOfConnections = 5;
            BufferSize = 8192;
            ConnectTimeoutMs = 5000;
            SendTimeoutMs = 5000;
            ReceiveTimeoutMs = 5000;
        }

        public int MaxNumberOfConnections { get; set; }
        public int BufferSize { get; set; }
        public int ConnectTimeoutMs { get; set; }
        public int SendTimeoutMs { get; set; }
        public int ReceiveTimeoutMs { get; set; }
    }
}
