namespace TplSockets
{
    public class RemoteServer
    {
        public RemoteServer()
        {
            TransferFolder = string.Empty;
            ConnectionInfo = new ConnectionInfo();
        }

        public RemoteServer(string ipAddress, int port)
        {
            TransferFolder = string.Empty;
            ConnectionInfo = new ConnectionInfo(ipAddress, port);
        }

        public string TransferFolder { get; set; }
        public ConnectionInfo ConnectionInfo { get; set; }
    }

}
