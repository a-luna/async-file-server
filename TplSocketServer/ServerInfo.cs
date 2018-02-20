namespace TplSocketServer
{
    using AaronLuna.Common.Http;
    using AaronLuna.Common.Network;
    using AaronLuna.Common.Result;
    using System.Threading.Tasks;

    public struct ServerInfo
    {
        public string LocalIpAddress { get; set; }
        public string PublicIpAddress { get; set; }
        public int Port { get; set; }
        public string TransferFolder { get; set; }
        
        public static async Task<Result<string>> GetPublicIpAddressAsync()
        {
            var urlContent = await HttpHelper.GetUrlContentAsStringAsync("http://icanhazip.com/").ConfigureAwait(false);

            var publicIpResult = IpAddressHelper.ParseSingleIPv4Address(urlContent);
            if (publicIpResult.Failure)
            {
                return Result.Fail<string>("Unable to determine public IP address, please verify this machine has access to the internet");
            }

            var publicIp = publicIpResult.Value;

            return Result.Ok(publicIp);
        }
    
        public string GetLocalEndPoint()
        {
            return $"{LocalIpAddress}:{Port}";
        }

        public string GetPublicEndPoint()
        {
            return $"{PublicIpAddress}:{Port}";
        }
    }

    public static class ServerInfoExtensions
    {
        public static bool IsEqualTo(this ServerInfo myInfo, ServerInfo otherInfo)
        {
            //// Transfer Folder not used to determine equality, ip address
            //// and port number combination  must be unique 

            if (!string.Equals(myInfo.LocalIpAddress, otherInfo.LocalIpAddress))
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
