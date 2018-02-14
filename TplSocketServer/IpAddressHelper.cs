namespace TplSocketServer
{
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;

    public static class IpAddressHelper
    {
        public static IPAddress GetLocalIpV4Address()
        {
            var ipHostInfo = Dns.GetHostEntry(Dns.GetHostName());
            var ipAddress = ipHostInfo.AddressList.Select(ip => ip)
                .FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork);

            return ipAddress;
        }
    }
}
