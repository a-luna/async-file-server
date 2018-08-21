namespace AaronLuna.AsyncSocketServer
{
    public class SocketSettings
    {
        public SocketSettings()
        {
            ListenBacklogSize = 5;
            BufferSize = 8192;
            SocketTimeoutInMilliseconds = 5000;
        }

        public int ListenBacklogSize { get; set; }
        public int BufferSize { get; set; }
        public int SocketTimeoutInMilliseconds { get; set; }
    }
}
