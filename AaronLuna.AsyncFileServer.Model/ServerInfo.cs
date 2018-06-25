namespace AaronLuna.AsyncFileServer.Model
{
    using System.Net;
    using System.Xml.Serialization;

    using Common.Enums;

    using Common.Network;

    public class ServerInfo
    {
        IPAddress _localIp;
        IPAddress _pubilcIp;
        IPAddress _sessionIp;

        public ServerInfo()
        {
            TransferFolder = string.Empty;
            PortNumber = 0;

            SessionIpAddress = IPAddress.Loopback;
            LocalIpAddress = IPAddress.Loopback;
            PublicIpAddress = IPAddress.Loopback;
        }

        public ServerInfo(IPAddress ipAddress, int portNumber)
        {
            TransferFolder = string.Empty;
            PortNumber = portNumber;

            SessionIpAddress = ipAddress;
            LocalIpAddress = IPAddress.Loopback;
            PublicIpAddress = IPAddress.Loopback;
            //InitializeConnection(ipAddress);
        }

        public ServerInfo(string ipAddress, int portNumber)
        {
            TransferFolder = string.Empty;
            PortNumber = portNumber;

            SessionIpAddress = NetworkUtilities.ParseSingleIPv4Address(ipAddress).Value;
            LocalIpAddress = IPAddress.Loopback;
            PublicIpAddress = IPAddress.Loopback;
            //InitializeConnection(sessionIp);
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
        public int PortNumber { get; set; }

        //void InitializeConnection(IPAddress ipAddress)
        //{
        //    SessionIpAddress = ipAddress;
        //    LocalIpAddress = IPAddress.Loopback;
        //    PublicIpAddress = IPAddress.Loopback;

        //    if (NetworkUtilities.GetAddressType(SessionIpAddress) == NetworkUtilities.AddressType.Public)
        //    {
        //        LocalIpAddress = SessionIpAddress;
        //    }
        //    else if (NetworkUtilities.GetAddressType(SessionIpAddress) == NetworkUtilities.AddressType.Private)
        //    {
        //        PublicIpAddress = SessionIpAddress;
        //    }
        //}
    }

    public static class ServerInfoExtensions
    {
        public static bool IsEqualTo(this ServerInfo myInfo, ServerInfo otherInfo)
        {
            //// Transfer Folder not used to determine equality, ip address
            //// and port number combination must be unique 

            var sessionIpSimilarity = NetworkUtilities.CompareTwoIpAddresses(myInfo.SessionIpAddress, otherInfo.SessionIpAddress);
            var localIpSimilarity = NetworkUtilities.CompareTwoIpAddresses(myInfo.LocalIpAddress, otherInfo.LocalIpAddress);
            var publicIpSimilarity = NetworkUtilities.CompareTwoIpAddresses(myInfo.PublicIpAddress, otherInfo.PublicIpAddress);
            var bothPortsMatch = myInfo.PortNumber == otherInfo.PortNumber;

            if (sessionIpSimilarity == IpAddressSimilarity.AllBytesMatch && bothPortsMatch)
            {
                return true;
            }

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
