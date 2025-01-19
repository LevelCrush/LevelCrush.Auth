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
}