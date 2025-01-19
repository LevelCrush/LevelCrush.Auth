using System.Text.Json.Serialization;

namespace auth_server.Models;

public class BungieValidationResponse
{
    [JsonPropertyName("access_token")] 
    public string AccessToken { get; set; } = "";
    
    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; } = "";
    
    [JsonPropertyName("membership_id")]
    public long MembershipId { get; set; }
}