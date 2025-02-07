using System.Runtime.Serialization;

namespace auth_server;

public class DiscordConfig
{

    [DataMember] 
    public string ClientId { get; set; } = string.Empty;

    [DataMember] 
    public string ClientSecret { get; set; } = string.Empty;
    
    [DataMember]
    public string BotToken { get; set; } = string.Empty;

    [DataMember] 
    public string RedirectUrl { get; set; } = string.Empty;

    [DataMember] 
    public string[] TargetServers { get; set; } = [];

    [DataMember]
    public string[] AdminRoles { get; set; } = [];
    
    [DataMember]
    public string[] ModeratorRoles { get; set; } = [];
    
    [DataMember]
    public string[] RetiredRoles { get; set; } = [];
    

    
    public static DiscordConfig Load()
    {
        var appConfig = new ConfigurationBuilder()
            .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
            .AddJsonFile("appsettings.json", false, false)
            .AddJsonFile("appsettings.test.json", true, false)
            .AddJsonFile("appsettings.development.json", true, false)
            .AddJsonFile("appsettings.staging.json", true, false)
            .AddJsonFile("appsettings.production.json", true, false)
            .AddJsonFile("appsettings.local.json", true, false)
            .Build();

        var section = appConfig.GetRequiredSection("Discord");
        var config = section.Get<DiscordConfig>();
        if (config == null)
        {
            throw new Exception("Discord configuration could not be loaded");
        }

        return config;
    }
}