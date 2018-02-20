namespace TplSocketServer
{
    public class SocketSettings
    {
        public int MaxNumberOfConections { get; set; }
        public int BufferSize { get; set; }
        public int ConnectTimeoutMs { get; set; }
        public int SendTimeoutMs { get; set; }
        public int ReceiveTimeoutMs { get; set; }

        public bool IsInitialized()
        {
            if (MaxNumberOfConections is 0)
            {
                return false;
            }

            if (BufferSize is 0)
            {
                return false;
            }

            if (ConnectTimeoutMs is 0)
            {
                return false;
            }

            if (ReceiveTimeoutMs is 0)
            {
                return false;
            }

            if (SendTimeoutMs is 0)
            {
                return false;
            }

            return true;
        }
    }
}
