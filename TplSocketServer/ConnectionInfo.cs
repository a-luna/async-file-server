namespace TplSocketServer
{
    using AaronLuna.Common.Network;
    using System.Net;

    public struct ConnectionInfo
    {
        public string LocalIpAddress { get; set; }
        public string PublicIpAddress { get; set; }
        public int Port { get; set; }       
        
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
            var parse = IpAddressHelper.ParseSingleIPv4Address(LocalIpAddress);

            return parse.Success
                ? parse.Value
                : IPAddress.None;
        }

        public IPAddress GetPublicIpAddress()
        {
            var parse = IpAddressHelper.ParseSingleIPv4Address(PublicIpAddress);

            return parse.Success
                ? parse.Value
                : IPAddress.None;
        }
    }

    public static class ServerInfoExtensions
    {
        public static bool IsEqualTo(this ConnectionInfo myInfo, ConnectionInfo otherInfo)
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
