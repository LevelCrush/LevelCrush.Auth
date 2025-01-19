using System.Text.Json.Serialization;

namespace auth_server.Models;

public class BungieMembership
{
    [JsonPropertyName("LastSeenDisplayName")]
    public string LastSeenDisplayName { get; set; } = String.Empty;


    [JsonPropertyName("applicableMembershipTypes")]
    public int[] ApplicationMembershipTypes { get; set; } = [];

    [JsonPropertyName("membershipId")] 
    public long MembershipId { get; set; } = 0;

    [JsonPropertyName("membershipType")] 
    public int MembershipType { get; set; } = 0;

    [JsonPropertyName("displayName")] 
    public string DisplayName { get; set; } = String.Empty;


    [JsonPropertyName("bungieGlobalDisplayName")]
    public string GlobalDisplayName { get; set; } = String.Empty;


    [JsonPropertyName("bungieGlobalDisplayNameCode")]
    public int GlobalDisplayNameCode { get; set; } = 0;
}