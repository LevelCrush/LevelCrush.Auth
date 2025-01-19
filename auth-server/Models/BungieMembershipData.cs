using System.Text.Json.Serialization;

namespace auth_server.Models;

public class BungieMembershipData
{
    [JsonPropertyName("destinyMemberships")]
    public BungieMembership[] Memberships { get; set; } = [];

    [JsonPropertyName("primaryMembershipId")]
    public long PrimaryMembershipId { get; set; } = 0;
    
    [JsonPropertyName("bungieNetUser")]
    public BungieUserData NetUser { get; set; } = new BungieUserData();
}