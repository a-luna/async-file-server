using System.Net;

namespace TplSocketServer
{
    using AaronLuna.Common.Http;
    using AaronLuna.Common.Network;
    using AaronLuna.Common.Result;
    using System.Threading.Tasks;

    public struct ServerInfo
    {
        public string LocalIpAddress { get; set; }
        public string PublicIpAddress { get; set; }
        public int Port { get; set; }
        public string TransferFolder { get; set; }
        
        public string GetLocalEndPoint()
        {
            return $"{LocalIpAddress}:{Port}";
        }

        public string GetPublicEndPoint()
        {
            return $"{PublicIpAddress}:{Port}";
        }

        public IPAddress GetLocalIpAddress()
        {
            return IPAddress.Parse(LocalIpAddress);
        }

        public IPAddress GetPublicIpAddress()
        {
            return IPAddress.Parse(PublicIpAddress);
        }
    }

    public static class ServerInfoExtensions
    {
        public static bool IsEqualTo(this ServerInfo myInfo, ServerInfo otherInfo)
        {
            //// Transfer Folder not used to determine equality, ip address
            //// and port number combination  must be unique 

            if (!string.Equals(myInfo.LocalIpAddress, otherInfo.LocalIpAddress))
            {
                return false;
            }

            if (myInfo.Port != otherInfo.Port)
            {
                return false;
            }

            return true;
        }
    }
}
