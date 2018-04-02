namespace TplSockets
{
    using System.Net;
    using System.Xml.Serialization;

    using AaronLuna.Common.Enums;
    using AaronLuna.Common.Network;

    public class ConnectionInfo
    {
        IPAddress _localIp;
        IPAddress _pubilcIp;
        IPAddress _sessionIp;

        public ConnectionInfo(IPAddress ipAddress, int port)
        {
            InitializeConnection(ipAddress, port);
        }

        public ConnectionInfo(string ipAddress, int port)
        {
            var sessionIp = Network.ParseSingleIPv4Address(ipAddress).Value;
            InitializeConnection(sessionIp, port);
        }

        public ConnectionInfo()
        {
            LocalIpAddress = IPAddress.None;
            PublicIpAddress = IPAddress.None;
            SessionIpAddress = IPAddress.None;

            LocalIpString = string.Empty;
            PublicIpString = string.Empty;
            SessionIpString = string.Empty;
        }

        [XmlIgnore]
        public IPAddress SessionIpAddress
        {
            get => _sessionIp;
            set
            {
                _sessionIp = value;
                SessionIpString = value.ToString();
            }
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

        public string SessionIpString { get; set; }
        public string LocalIpString { get; set; }
        public string PublicIpString { get; set; }
        public int Port { get; set; }

        void InitializeConnection(IPAddress ipAddress, int port)
        {
            var sessionIp = ipAddress;
            var localIp = IPAddress.None;
            var publicIp = IPAddress.None;

            if (Network.IpAddressIsInPrivateAddressSpace(ipAddress))
            {
                localIp = sessionIp;
            }
            else
            {
                publicIp = sessionIp;
            }

            Port = port;
            SessionIpAddress = sessionIp;
            LocalIpAddress = localIp;
            PublicIpAddress = publicIp;
        }
    }

    public static class ConnectionInfoExtensions
    {
        public static bool IsEqualTo(this ConnectionInfo myInfo, ConnectionInfo otherInfo)
        {
            //// Transfer Folder not used to determine equality, ip address
            //// and port number combination must be unique 

            var localIpSimilarity = Network.CompareTwoIpAddresses(myInfo.LocalIpAddress, otherInfo.LocalIpAddress);
            var publicIpSimilarity = Network.CompareTwoIpAddresses(myInfo.PublicIpAddress, otherInfo.PublicIpAddress);
            var bothPortsMatch = myInfo.Port == otherInfo.Port;

            if (publicIpSimilarity == IpAddressSimilarity.AllBytesMatch && bothPortsMatch)
            {
                return true;
            }

            if (localIpSimilarity == IpAddressSimilarity.AllBytesMatch && bothPortsMatch)
            {
                return true;
            }

            return false;
        }
    }

}
