using System.Net;
using RestSharp;

namespace auth_server;

public class DiscordClient
{
    public static RestClient Client { get; private set; }

    static DiscordClient()
    {
        Client = new RestClient(new RestClientOptions()
        {
            FollowRedirects = true
        });
    }
    
    public static async Task<T?> Get<T>(string endpoint, string accessToken) where T: class
    {

        var maxAttempts = 10;
        var attempt = 0;
        do
        {
            var req = new RestRequest($"https://discord.com/api/v10/{endpoint}");
        
            req.AddHeader("Authorization", "Bearer " + accessToken);
        
            var res = await Client.ExecuteAsync<T>(req);
            
            if (res.StatusCode == HttpStatusCode.OK)
            {
                // force throttle all request. Before returning any data. Throttle by one second. 
                await Task.Delay(TimeSpan.FromSeconds(1));
                return res.Data;
            } else if (res.StatusCode == HttpStatusCode.TooManyRequests && res.Headers != null)
            {
                var retryAfterString = res.Headers.Where(x => x.Name.ToLower() == "retry-after")
                    .Select(x => x.Value)
                    .FirstOrDefault();

                var retryAfterSeconds = 0;
                int.TryParse(retryAfterString, out retryAfterSeconds);
                LoggerGlobal.Write($"Attempting to retry discord request after {retryAfterSeconds} seconds");
                await Task.Delay(TimeSpan.FromSeconds(retryAfterSeconds));
            }
            else
            {
                break;
            }
            
        } while (attempt++ < maxAttempts);

        return null;
    }
}