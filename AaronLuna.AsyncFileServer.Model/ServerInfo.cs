namespace AaronLuna.AsyncFileServer.Model
{
    using System;
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
            Name = string.Empty;
            TransferFolder = string.Empty;
            PortNumber = 0;

            SessionIpAddress = IPAddress.Loopback;
            LocalIpAddress = IPAddress.Loopback;
            PublicIpAddress = IPAddress.Loopback;

            SessionIpString = string.Empty;
            LocalIpString = string.Empty;
            PublicIpString = string.Empty;
        }

        public ServerInfo(IPAddress ipAddress, int portNumber)
        {
            Name = string.Empty;
            TransferFolder = string.Empty;
            PortNumber = portNumber;

            SessionIpAddress = ipAddress;
            LocalIpAddress = IPAddress.Loopback;
            PublicIpAddress = IPAddress.Loopback;

            SessionIpString = string.Empty;
            LocalIpString = string.Empty;
            PublicIpString = string.Empty;
        }

        public ServerInfo(string ipAddress, int portNumber)
        {
            Name = string.Empty;
            TransferFolder = string.Empty;
            PortNumber = portNumber;

            SessionIpAddress = NetworkUtilities.ParseSingleIPv4Address(ipAddress).Value;
            LocalIpAddress = IPAddress.Loopback;
            PublicIpAddress = IPAddress.Loopback;

            SessionIpString = string.Empty;
            LocalIpString = string.Empty;
            PublicIpString = string.Empty;
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

        public string Name { get; set; }
        public string SessionIpString { get; set; }
        public string LocalIpString { get; set; }
        public string PublicIpString { get; set; }
        public int PortNumber { get; set; }
        public ServerPlatform Platform { get; set; }

        public override string ToString()
        {
            var endpoint = $"{SessionIpAddress}:{PortNumber}";
            var serverInfo = $"{Name} ({endpoint})";

            return string.IsNullOrEmpty(Name)
                ? endpoint
                : serverInfo;
        }

        public string ItemText =>
        $"Name......: {Name}{Environment.NewLine}" +
        $"   IP........: {SessionIpAddress}{Environment.NewLine}" +
        $"   Port......: {PortNumber}{Environment.NewLine}" +
        $"   Platform..: {Platform}{Environment.NewLine}";

        public void DetermineSessionIpAddress(string lanCidrIp)
        {
            SessionIpAddress = PublicIpAddress;

            var checkLocalIp = LocalIpAddress.IsInRange(lanCidrIp);
            if (checkLocalIp.Success && checkLocalIp.Value)
            {
                SessionIpAddress = LocalIpAddress;
            }
        }
    }

    public static class ServerInfoExtensions
    {
        public static bool IsEqualTo(this ServerInfo my, ServerInfo other)
        {
            // If port #s do not match, this is not the same server
            if (my.PortNumber != other.PortNumber) return false;
            
            // If the port #s and any IP are the same, the servers are considered equal
            if (my.SessionIpAddress.IsEqualTo(other.SessionIpAddress)) return true;
            if (my.SessionIpAddress.IsEqualTo(other.LocalIpAddress)) return true;
            if (my.SessionIpAddress.IsEqualTo(other.PublicIpAddress)) return true;

            if (my.LocalIpAddress.IsEqualTo(other.SessionIpAddress)) return true;
            if (my.LocalIpAddress.IsEqualTo(other.PublicIpAddress)) return true;

            if (my.PublicIpAddress.IsEqualTo(other.SessionIpAddress)) return true;
            if (my.PublicIpAddress.IsEqualTo(other.PublicIpAddress)) return true;

            // If the port #s are the same but all IPs are unique, this is not the same server
            return false;
        }
    }
}
