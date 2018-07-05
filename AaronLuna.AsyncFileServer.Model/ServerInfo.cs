namespace AaronLuna.AsyncFileServer.Model
{
    using System.Net;
    using System.Xml.Serialization;

    using Common.Extensions;
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
        }

        public ServerInfo(string ipAddress, int portNumber)
        {
            TransferFolder = string.Empty;
            PortNumber = portNumber;

            SessionIpAddress = NetworkUtilities.ParseSingleIPv4Address(ipAddress).Value;
            LocalIpAddress = IPAddress.Loopback;
            PublicIpAddress = IPAddress.Loopback;
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

        public override string ToString()
        {
            return $"{SessionIpAddress}:{PortNumber}";
        }
    }

    public static class ServerInfoExtensions
    {
        public static bool IsEqualTo(this ServerInfo my, ServerInfo other)
        {
            var portsDiffer = my.PortNumber != other.PortNumber;
            if (portsDiffer) return false;

            var sessionIpsMatch = my.SessionIpAddress.IsEqualTo(other.SessionIpAddress);
            var localIpsMatch = my.LocalIpAddress.IsEqualTo(other.LocalIpAddress);
            var publicIpsMatch = my.PublicIpAddress.IsEqualTo(other.PublicIpAddress);

            return sessionIpsMatch || localIpsMatch || publicIpsMatch;
        }
    }

}
