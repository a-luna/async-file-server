﻿namespace TplSocketServer
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

        public ConnectionInfo()
        {
            LocalIpAddress = IPAddress.None;
            PublicIpAddress = IPAddress.None;
            SessionIpAddress = IPAddress.None;

            LocalIpString = string.Empty;
            PublicIpString = string.Empty;
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
            //// and port number combination must be unique 

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
