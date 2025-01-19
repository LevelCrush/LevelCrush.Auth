using System.Text.Json.Serialization;

namespace auth_server.Models;

public class BungieValidationResult
{
    [JsonPropertyName("membershipId")] 
    public string MembershipId { get; set; } = String.Empty;

    [JsonPropertyName("membershipType")] 
    public int MembershipType { get; set; } = 0;
    
    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = String.Empty;
    
    [JsonPropertyName("inNetworkClan")]
    public bool InNetworkClan { get; set; } = false;
    
}