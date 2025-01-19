using System.Text.Json.Serialization;

namespace auth_server.Models;

public class BungieUserData
{
    [JsonPropertyName("membershipId")] 
    public long membership_id { get; set; }

    [JsonPropertyName("uniqueName")] 
    public string UniqueName { get; set; } = String.Empty;

    [JsonPropertyName("displayName")] 
    public string DisplayName { get; set; } = String.Empty;

    [JsonPropertyName("cachedBungieGlobalDisplayName")]
    public string GlobalDisplayName { get; set; } = String.Empty;

    [JsonPropertyName("cachedBungieGlobalDisplayNameCode")]
    public int GlobalDisplayNameCode { get; set; } = 0;
}

