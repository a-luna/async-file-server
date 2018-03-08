using System.Xml.Serialization;
using AaronLuna.Common.Enums;

namespace TplSocketServer
{
    using AaronLuna.Common.Network;
    using System.Net;

    public class ConnectionInfo
    {
        private IPAddress _localIp;
        private IPAddress _pubilcIp;

        public ConnectionInfo()
        {
            LocalIpAddress = IPAddress.None;
            PublicIpAddress = IPAddress.None;

            LocalIpString = string.Empty;
            PublicIpString = string.Empty;
        }

        [XmlIgnore]
        public IPAddress LocalIpAddress
        {
            get => _localIp;
            set
            {
                _localIp = value;
                LocalIpString = value.ToString();
            }
        }

        [XmlIgnore]
        public IPAddress PublicIpAddress
        {
            get => _pubilcIp;
            set
            {
                _pubilcIp = value;
                PublicIpString = value.ToString();
            }
        }

        public string LocalIpString { get; set; }

        public string PublicIpString { get; set; }

        public int Port { get; set; }

        public string GetPublicEndPoint()
        {
            return $"{PublicIpString}:{Port}";
        }

        public byte[] GetLocalIpAddressBytes()
        {
            return LocalIpAddress.GetAddressBytes();
        }

        public byte[] GetPublicIpAddressBytes()
        {
            return PublicIpAddress.GetAddressBytes();
        }
    }

    public static class ConnectionInfoExtensions
    {
        public static bool IsEqualTo(this ConnectionInfo myInfo, ConnectionInfo otherInfo)
        {
            //// Transfer Folder not used to determine equality, ip address
            //// and port number combination  must be unique 

            var localIpSimilarity = Network.CompareTwoIpAddresses(myInfo.LocalIpAddress, otherInfo.LocalIpAddress);
            var publicIpSimilarity = Network.CompareTwoIpAddresses(myInfo.PublicIpAddress, otherInfo.PublicIpAddress);

            if (localIpSimilarity == IpAddressSimilarity.AllBytesMatch && myInfo.Port == otherInfo.Port)
            {
                return true;
            }

            if (publicIpSimilarity == IpAddressSimilarity.AllBytesMatch && myInfo.Port == otherInfo.Port)
            {
                return true;
            }

            return false;
        }
    }
}
