using System;
namespace TplSocketServer
{
    using System.Net.Http;
    using System.Threading.Tasks;

    public class HttpHelper
    {
        public static async Task<string> GetUrlContentAsStringAsync(string url)
        {
            using (var httpClient = new HttpClient())
            using (var httpResonse = await httpClient.GetAsync(url).ConfigureAwait(false))
            {
                return await httpResonse.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
        }
    }
}
