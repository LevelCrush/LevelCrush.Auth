using System.Text.Json.Serialization;

namespace auth_server.Models;

public class DiscordValidationResult
{
    [JsonPropertyName("discordHandle")]
    public string Handle { get; set; } = string.Empty;

    [JsonPropertyName("discordId")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("inServer")]
    public bool InServer { get; set; } = false;
    
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;
    
    [JsonPropertyName("nicknames")] 
    public string[] Nicknames { get; set; } = [];
    
    [JsonPropertyName("isAdmin")]
    public bool IsAdmin { get; set; } = false;
    
    [JsonPropertyName("isModerator")]
    public bool IsModerator { get; set; } = false;
}