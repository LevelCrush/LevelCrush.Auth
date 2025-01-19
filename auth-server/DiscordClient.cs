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

        var req = new RestRequest($"https://discord.com/api/v10/{endpoint}");
        
        req.AddHeader("Authorization", "Bearer " + accessToken);
        
        var res = await Client.ExecuteAsync<T>(req);
        if (res.IsSuccessful)
        {
            return res.Data;
        }
        else
        {
            return null;
        }
    }
}