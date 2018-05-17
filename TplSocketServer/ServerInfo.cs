using AaronLuna.Common.Enums;

namespace TplSockets
{
    using System.Net;
    using System.Xml.Serialization;

    using AaronLuna.Common.Network;

    public class ServerInfo
    {
        IPAddress _localIp;
        IPAddress _pubilcIp;
        IPAddress _sessionIp;

        public ServerInfo()
        {
            LocalIpAddress = IPAddress.None;
            PublicIpAddress = IPAddress.None;
            SessionIpAddress = IPAddress.None;

            LocalIpString = string.Empty;
            PublicIpString = string.Empty;
            SessionIpString = string.Empty;
            TransferFolder = string.Empty;
        }
        
        public ServerInfo(IPAddress ipAddress, int port)
        {
            TransferFolder = string.Empty;
            InitializeConnection(ipAddress, port);
        }

        public ServerInfo(string ipAddress, int port)
        {
            TransferFolder = string.Empty;
            var sessionIp = NetworkUtilities.ParseSingleIPv4Address(ipAddress).Value;
            InitializeConnection(sessionIp, port);
        }

        public string TransferFolder { get; set; }

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
            SessionIpAddress = ipAddress;
            LocalIpAddress = IPAddress.Loopback;
            PublicIpAddress = IPAddress.Loopback;
            Port = port;

            switch (NetworkUtilities.GetAddressType(SessionIpAddress))
            {
                case NetworkUtilities.AddressType.Public:
                    LocalIpAddress = ipAddress;
                    break;

                case NetworkUtilities.AddressType.Private:
                    PublicIpAddress = ipAddress;
                    break;
            }
        }
    }

    public static class RemoteServerExtensions
    {
        public static bool IsEqualTo(this ServerInfo myInfo, ServerInfo otherInfo)
        {
            //// Transfer Folder not used to determine equality, ip address
            //// and port number combination must be unique 

            var localIpSimilarity = NetworkUtilities.CompareTwoIpAddresses(myInfo.LocalIpAddress, otherInfo.LocalIpAddress);
            var publicIpSimilarity = NetworkUtilities.CompareTwoIpAddresses(myInfo.PublicIpAddress, otherInfo.PublicIpAddress);
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
