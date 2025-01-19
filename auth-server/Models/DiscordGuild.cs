using System.Text.Json.Serialization;

namespace auth_server.Models;

public class DiscordGuild
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = String.Empty;
    
    [JsonPropertyName("name")]
    public string Name { get; set; } = String.Empty;
    
    [JsonPropertyName("owner")]
    public bool Owner { get; set; } = false;
}