namespace TplSocketServer
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Net.Sockets;
    using System.Text.RegularExpressions;

    public static class IpAddressHelper
    {
        public static List<IPAddress> GetAllLocalIpv4Addresses()
        {
            var ipHostInfo = Dns.GetHostEntry(Dns.GetHostName());

            var ips = 
                ipHostInfo.AddressList.Select(ip => ip)
                    .Where(ip => ip.AddressFamily == AddressFamily.InterNetwork).ToList();

            return ips;

        }

        public static IPAddress GetLocalIpV4Address()
        {
            var ipHostInfo = Dns.GetHostEntry(Dns.GetHostName());
            var ipAddress = ipHostInfo.AddressList.Select(ip => ip)
                .FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork);

            return ipAddress;
        }

        public static Result<string> ParseSingleIPv4Address(string input)
        {
            var parsedIpsResult = ParseAllIPv4Addresses(input);
            if (parsedIpsResult.Failure)
            {
                return Result.Fail<string>($"Unable tp parse IPv4 addressf rom input string: {parsedIpsResult.Error}");
            }

            var ips = parsedIpsResult.Value;
            var firstIp = ips.FirstOrDefault();

            return Result.Ok(firstIp);
        }

        public static Result<List<string>> ParseAllIPv4Addresses(string input)
        {
            if (input == null)
            {
                return Result.Fail<List<string>>("RegEx string cannot be null");
            }

            const string pattern =
                @"((25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)";

            var ips = new List<string>();
            try
            {
                var regex = new Regex(pattern);
                foreach (Match match in regex.Matches(input))
                {
                    ips.Add(match.Value);
                }
            }
            catch (RegexMatchTimeoutException ex)
            {
                var exceptionType = ex.GetType();
                var exceptionMessage = ex.Message;

                return Result.Fail<List<string>>($"{exceptionMessage} ({exceptionType})");
            }

            if (ips.Count == 0)
            {
                return Result.Fail<List<string>>("Input string did not contain any valid IPv4 addreses");
            }

            return Result.Ok(ips);
        }
    }
}
