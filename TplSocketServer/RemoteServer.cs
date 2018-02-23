namespace TplSocketServer
{
    public class RemoteServer
    {
        public RemoteServer()
        {
            TransferFolder = string.Empty;
            ConnectionInfo = new ConnectionInfo();
        }

        public string TransferFolder { get; set; }
        public ConnectionInfo ConnectionInfo { get; set; }
    }
}
