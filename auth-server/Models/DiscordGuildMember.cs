using System.Text.Json.Serialization;

namespace auth_server.Models;

public class DiscordGuildMember
{
    [JsonPropertyName("user")] 
    public DiscordUserResponse? User { get; set; } 
    
    [JsonPropertyName("nick")]
    public string Nickname { get; set; } = string.Empty;
    
    [JsonPropertyName("avatar")]
    public string Avatar { get; set; } = string.Empty;
    
    [JsonPropertyName("joined_at")]
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UnixEpoch;

    [JsonPropertyName("premium_since")] 
    public DateTimeOffset? PremiumSince { get; set; }

    [JsonPropertyName("communication_disabled_until")]
    public DateTimeOffset? CommunicationDisabledUntil { get; set; }

    [JsonPropertyName("roles")] 
    public string[] Roles { get; set; } = [];
}