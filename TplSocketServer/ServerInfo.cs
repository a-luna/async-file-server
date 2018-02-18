namespace TplSocketServer
{
    public struct ServerInfo
    {
        public string IpAddress { get; set; }
        public int Port { get; set; }
        public string TransferFolder { get; set; }

        public string GetEndPoint()
        {
            return $"{IpAddress}:{Port}";
        }

        public static Result<ServerInfo> GetServerInfo(string fileLine)
        {
            var split = fileLine.Split('*');
            if (split.Length != 3)
            {
                return Result.Fail<ServerInfo>($"Unable to parse server connection info from file string: {fileLine}");
            }

            var getIpAddress = split[0].GetAllIPv4AddressesInString();
            if (getIpAddress.Failure)
            {
                return Result.Fail<ServerInfo>($"Unable to parse IP address from file string: {fileLine}");
            }

            var parsedIps = getIpAddress.Value;

            if (parsedIps.Count == 0)
            {
                return Result.Fail<ServerInfo>($"Zero valid IP addresses found in file string: {fileLine}");
            }

            if (parsedIps.Count > 1)
            {
                return Result.Fail<ServerInfo>($"Parsed more than one IP address from file string: {fileLine}");
            }

            if (!int.TryParse(split[1], out int port))
            {
                return Result.Fail<ServerInfo>($"Unable to parse port number from file string: {fileLine}");
            }

            var serverInfo = new ServerInfo
            {
                IpAddress = parsedIps[0],
                Port = port,
                TransferFolder = split[2]
            };

            return Result.Ok(serverInfo);
        }
    }

    public static class ServerInfoExtensions
    {
        public static bool IsEqualTo(this ServerInfo myInfo, ServerInfo otherInfo)
        {
            //// Transfer Folder not used to determine equality, ip address
            //// and port number combination  must be unique 

            if (!string.Equals(myInfo.IpAddress, otherInfo.IpAddress))
            {
                return false;
            }

            if (myInfo.Port != otherInfo.Port)
            {
                return false;
            }

            return true;
        }
    }
}
