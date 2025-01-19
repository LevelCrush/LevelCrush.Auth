using System.Text.Json.Serialization;

namespace auth_server.Models;

public class DiscordUserResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    
    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;
    
    [JsonPropertyName("discriminator")]
    public string Discriminator { get; set; } = string.Empty;
    
    [JsonPropertyName("avatar")]
    public string Avatar { get; set; } = string.Empty;
    
    [JsonPropertyName("global_name")]
    public string GlobalName { get; set; } = string.Empty;
    
    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = string.Empty;
}