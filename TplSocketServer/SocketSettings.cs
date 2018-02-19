namespace TplSocketServer
{
    public class SocketSettings
    {
        public int MaxNumberOfConections { get; set; }
        public int BufferSize { get; set; }
        public int ConnectTimeoutMs { get; set; }
        public int SendTimeoutMs { get; set; }
        public int ReceiveTimeoutMs { get; set; }
    }
}
