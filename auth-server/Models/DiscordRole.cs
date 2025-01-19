using System.Text.Json.Serialization;

namespace auth_server.Models;

public class DiscordRole
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}