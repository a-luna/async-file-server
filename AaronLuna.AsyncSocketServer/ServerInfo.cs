using System;
using System.Net;
using System.Xml.Serialization;
using AaronLuna.Common.Extensions;
using AaronLuna.Common.Network;

namespace AaronLuna.AsyncSocketServer
{
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

        public ServerInfo(IPAddress ipAddress, int portNumber) :this()
        {
            SessionIpAddress = ipAddress;
            PortNumber = portNumber;
        }

        public ServerInfo(string ipAddress, int portNumber) :this()
        {
            SessionIpAddress = NetworkUtilities.ParseSingleIPv4Address(ipAddress).Value;
            PortNumber = portNumber;
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

        public string Name { get; set; }
        public string SessionIpString { get; set; }
        public string LocalIpString { get; set; }
        public string PublicIpString { get; set; }
        public int PortNumber { get; set; }
        public string TransferFolder { get; set; }
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

        public void InitializeIpAddresses()
        {
            var localIp = LocalIpString;
            var publicIp = PublicIpString;
            var sessionIp = SessionIpString;

            LocalIpAddress = IPAddress.None;
            PublicIpAddress = IPAddress.None;
            SessionIpAddress = IPAddress.None;

            if (!string.IsNullOrEmpty(localIp))
            {
                var parseLocalIpResult = NetworkUtilities.ParseSingleIPv4Address(localIp);
                if (parseLocalIpResult.Success)
                {
                    LocalIpAddress = parseLocalIpResult.Value;
                }
            }

            if (!string.IsNullOrEmpty(publicIp))
            {
                var parsePublicIpResult = NetworkUtilities.ParseSingleIPv4Address(publicIp);
                if (parsePublicIpResult.Success)
                {
                    PublicIpAddress = parsePublicIpResult.Value;
                }
            }

            if (!string.IsNullOrEmpty(sessionIp))
            {
                var parseSessionIpResult = NetworkUtilities.ParseSingleIPv4Address(sessionIp);
                if (parseSessionIpResult.Success)
                {
                    SessionIpAddress = parseSessionIpResult.Value;
                }
            }
        }

        public ServerInfo Duplicate()
        {
            var shallowCopy = (ServerInfo) MemberwiseClone();

            shallowCopy.Name = string.Copy(Name);
            shallowCopy.SessionIpString = string.Copy(SessionIpString);
            shallowCopy.PublicIpString = string.Copy(PublicIpString);
            shallowCopy.LocalIpString = string.Copy(LocalIpString);
            shallowCopy.TransferFolder = string.Copy(TransferFolder);
            InitializeIpAddresses();

            return shallowCopy;
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
