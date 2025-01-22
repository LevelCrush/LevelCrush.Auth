using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using auth_server;
using auth_server.Models;
using Destiny;
using Destiny.Api;
using Destiny.Models.Enums;
using Microsoft.AspNetCore.Mvc;
using RestSharp;
using RestSharp.Authenticators;

var builder = WebApplication.CreateBuilder(args);

// load api configuration information
var _DestinyConfig = DestinyConfig.Load();
BungieClient.ApiKey = _DestinyConfig.ApiKey;

// load discord configuration
var _DiscordConfig = DiscordConfig.Load();

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(15);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.None;
    //options.Cookie.Domain = "levelcrush.com"
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyHeader()
            .AllowAnyMethod()
            .SetIsOriginAllowed(origin => true)
            .AllowCredentials();/*AllowCredentials()


            .WithOrigins("levelcrush.com",
                "www.levelcrush.com",
                "account.levelcrush.com",
                "accounts.levelcrush.com",
                "assets.levelcrush.com",
                "spt.levelcrush.com",
                "dev-levelcrush.myshopify.com",
                "dev-auth.levelcrush.com",
                "auth.levelcrush.com",
                "shopify.com",
                "trycloudflare.com") */
        
    });
    
});


var app = builder.Build();

app.UseRouting();
app.UseSession();
app.UseCors();



// Start Bungie Linking

// remove all bungie/destiny related keys
app.MapGet("/platform/bungie/logout", (HttpRequest httpRequest) =>
{
    httpRequest.HttpContext.Session.Remove("Destiny.MembershipID");
    httpRequest.HttpContext.Session.Remove("Destiny.Clan");
    httpRequest.HttpContext.Session.Remove("Destiny.MembershipPlatform");
    httpRequest.HttpContext.Session.Remove("Destiny.DisplayName");
    httpRequest.HttpContext.Session.Remove("BungieState");

    return Results.Text("200 OK");
});

// from our session retrieve bungie/destiny related session information
app.MapGet("/platform/bungie/session", (HttpRequest httpRequest) =>
{
    
    var membershipId = httpRequest.HttpContext.Session.GetString("Destiny.MembershipID");
    var inClan = httpRequest.HttpContext.Session.GetInt32("Destiny.Clan") == 1;
    var membershipPlatform = httpRequest.HttpContext.Session.GetInt32("Destiny.MembershipPlatform");
    var displayName = httpRequest.HttpContext.Session.GetString("Destiny.DisplayName");

    return Results.Json(new BungieValidationResult()
    {
        MembershipId = membershipId ?? String.Empty,
        InNetworkClan = inClan,
        MembershipType = membershipPlatform ?? -1,
        DisplayName = displayName ?? String.Empty,
    });

});


// start the login process to use Official Bungie OAuth
app.MapGet("/platform/bungie/login",  async (HttpRequest httpReq) =>
{

    httpReq.Query.TryGetValue("token", out var tokenValues);
    var token = tokenValues.FirstOrDefault();
    if (token == null)
    {
        token = "";
    }
    
    httpReq.HttpContext.Session.SetString("BungieXToken", token);
    var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    
    // technically we can do better here...but for now this works
    var hashResults = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes($"{token}||{timestamp}")));
    var bungieState = hashResults;
    httpReq.HttpContext.Session.SetString("BungieState", bungieState);
    
    var authorizeUrl =
        $"https://www.bungie.net/en/OAuth/Authorize?response_type=code&client_id={HttpUtility.UrlEncode(_DestinyConfig.ClientId)}&state={bungieState}&prompt=prompt";
    return Results.Redirect(authorizeUrl, false, false);
});

// validate the codes that came back from the oauth request and store into session the required information
app.MapGet("/platform/bungie/validate", async (HttpRequest httpRequest) =>
{

    httpRequest.Query.TryGetValue("code", out var oauthCodeValues);
    httpRequest.Query.TryGetValue("error", out var oauthErrorValues);
    httpRequest.Query.TryGetValue("state", out var oauthStateValues);
        
    var oauthCode = oauthCodeValues.FirstOrDefault();
    var oauthError = oauthErrorValues.FirstOrDefault();
    var oauthState = oauthStateValues.FirstOrDefault();
    
    var doProcess = true;
    
    
    var token = httpRequest.HttpContext.Session.GetString("BungieXToken");
    if (token == null)
    {
        token = "";
        doProcess = false;
    }

    if (oauthError != null && oauthError.Length > 0)
    {
        LoggerGlobal.Write($"There was an error found in the oauth request.\n{oauthError}", LogLevel.Error);
        doProcess = false;
    }
   
    if (oauthCode == null && (oauthCode != null && oauthCode.Length == 0))
    {
        LoggerGlobal.Write($"There was no oauth code found in the request", LogLevel.Error);
        doProcess = false;
    }


    var sessionState = httpRequest.HttpContext.Session.GetString("BungieState");
    if (oauthState == null || (oauthState != null && oauthState != sessionState))
    {
        LoggerGlobal.Write($"States are mismatching. Bungie: {oauthState} || Session: {sessionState}", LogLevel.Error);
        doProcess = false;
    }

    if (!doProcess)
    {
        return Results.Text("Failed security checks. Bad Request");
    }

    var req = new RestRequest("https://www.bungie.net/Platform/App/OAuth/token/");
    req.Method = Method.Post;
    req.AddHeader("Content-Type", "application/x-www-form-urlencoded");
    req.AddHeader("X-API-KEY", _DestinyConfig.ApiKey);
    req.AddHeader("Accept", "application/json");
    
    req.AddParameter("grant_type", "authorization_code");
    req.AddParameter("code", oauthCode);
    
    req.Authenticator = new HttpBasicAuthenticator(_DestinyConfig.ClientId, _DestinyConfig.ClientSecret);

    var response = await BungieClient.Client.ExecuteAsync<BungieValidationResponse>(req);

    BungieValidationResponse? validationResponse = null;
    if (response.IsSuccessful && response.Data != null)
    {
        validationResponse = response.Data;
    }
    else
    {
        LoggerGlobal.Write($"Bungie Request Error: {response.ErrorMessage}");
        doProcess = false;
    }

    long membershipId = 0;
    var membershipPlatform = -1;
    var membershipDisplayName = "";
    if (doProcess)
    {
        var userReq = await BungieClient.Get($"/User/GetMembershipsById/{response.Data.MembershipId}/-1/")
            .Send<BungieMembershipData>();

        if (userReq.Response != null)
        {
            membershipId = userReq.Response.PrimaryMembershipId;
            membershipDisplayName = userReq.Response.NetUser.UniqueName; // this is the Bungie Global Display Name with code. Dont get confused by the fields.

            foreach (var profileMembership in userReq.Response.Memberships)
            {
                if (profileMembership.MembershipId == membershipId)
                {
                    membershipPlatform = profileMembership.MembershipType;
                    break;
                }
            } 
        }
        else
        {
            doProcess = false;
        }
    }

    var inClan = false;
    if (doProcess)
    {
        var clanReq = await DestinyClan.FromMembership(membershipId, (BungieMembershipType)membershipPlatform);
        if (clanReq != null && clanReq.Results.Length > 0)
        {
            var clan = clanReq.Results.FirstOrDefault();
            if (clan != null)
            {
                var clanGroupId = clan.Group.GroupId;
                // this should be long, this may cause problems sometime in the future. Maybe?
                inClan = _DestinyConfig.NetworkClans.Contains((int)clanGroupId);
            } 
        }
    }
    
    
    httpRequest.HttpContext.Session.SetString("Destiny.MembershipID", membershipId.ToString());
    httpRequest.HttpContext.Session.SetInt32("Destiny.Clan", inClan ? 1 : 0);
    httpRequest.HttpContext.Session.SetInt32("Destiny.MembershipPlatform", membershipPlatform);
    httpRequest.HttpContext.Session.SetString("Destiny.DisplayName", membershipDisplayName);
    
    var html =
        $"<!DOCTYPE html><html><head><title>Auth | Level Crush</title></head><body><p>Validated. You can close this window now.</p><script>window.close();</script></body><html>";
    
    httpRequest.HttpContext.Response.ContentType = "text/html";
    httpRequest.HttpContext.Response.ContentLength = Encoding.UTF8.GetByteCount(html);

    return Results.Content(html);

}); 

// End Bungie Linking


// Start Discord Linking

app.MapGet("/platform/discord/login", (HttpRequest httpReq) =>
{

    httpReq.Query.TryGetValue("token", out var tokenValues);
    var token = tokenValues.FirstOrDefault();
    if (token == null)
    {
        token = "";
    }
    
    httpReq.HttpContext.Session.SetString("Discord.XToken", token);
    var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    
    // technically we can do better here...but for now this works
    var hashResults = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes($"{token}||{timestamp}")));
    var discordState = hashResults;
    httpReq.HttpContext.Session.SetString("Discord.State", discordState);

    var scopes = new string[] { "identify", "guilds", "email", "guilds.members.read" };
    
    var authorizeUrl =
        $"https://discord.com/api/oauth2/authorize?response_type=code&client_id={HttpUtility.UrlEncode(_DiscordConfig.ClientId)}&scope={String.Join('+', scopes)}&state={discordState}&redirect_uri={HttpUtility.UrlEncode(_DiscordConfig.RedirectUrl)}&prompt=none";
    return Results.Redirect(authorizeUrl, false, false);
});

app.MapGet("/platform/discord/validate", async (HttpRequest httpRequest) =>
{
    httpRequest.Query.TryGetValue("code", out var oauthCodeValues);
    httpRequest.Query.TryGetValue("error", out var oauthErrorValues);
    httpRequest.Query.TryGetValue("state", out var oauthStateValues);
        
    var oauthCode = oauthCodeValues.FirstOrDefault();
    var oauthError = oauthErrorValues.FirstOrDefault();
    var oauthState = oauthStateValues.FirstOrDefault();
    
    var doProcess = true;
    
    
    var token = httpRequest.HttpContext.Session.GetString("Discord.XToken");
    if (token == null)
    {
        token = "";
        doProcess = false;
    }

    if (oauthError != null && oauthError.Length > 0)
    {
        LoggerGlobal.Write($"There was an error found in the oauth request.\n{oauthError}", LogLevel.Error);
        doProcess = false;
    }
   
    if (oauthCode == null && (oauthCode != null && oauthCode.Length == 0))
    {
        LoggerGlobal.Write($"There was no oauth code found in the request", LogLevel.Error);
        doProcess = false;
    }


    var sessionState = httpRequest.HttpContext.Session.GetString("Discord.State");
    if (oauthState == null || (oauthState != null && oauthState != sessionState))
    {
        LoggerGlobal.Write($"States are mismatching. Discord: {oauthState} || Session: {sessionState}", LogLevel.Error);
        doProcess = false;
    }

    if (!doProcess)
    {
        return Results.Text("Failed security checks. Bad Request");
    }

    var scopes = new string[] { "identify", "guilds", "email", "guilds.members.read" };

    var req = new RestRequest("https://discord.com/api/oauth2/token");
    req.Method = Method.Post;
    req.AddHeader("Content-Type", "application/x-www-form-urlencoded");
    req.AddHeader("Accept", "application/json");
    
    req.AddParameter("client_id", _DiscordConfig.ClientId);
    req.AddParameter("client_secret", _DiscordConfig.ClientSecret);
    req.AddParameter("grant_type", "authorization_code");
    req.AddParameter("code", oauthCode);
    req.AddParameter("redirect_uri", _DiscordConfig.RedirectUrl);
    req.AddParameter("scope", String.Join('+', scopes));

    //reuse the raw RestSharp client of the bungie client. This is safe since its the raw http client with no modificationbs
    var oauthResponse = await BungieClient.Client.ExecuteAsync<DiscordValidationResponse>(req);
    var accessToken = "";
    if (oauthResponse.IsSuccessful && oauthResponse.Data != null)
    {
        accessToken = oauthResponse.Data.AccessToken;
    }
    else
    {
        return Results.Text("Failed to login. Please try again.");
    }

    var userResponse = await DiscordClient.Get<DiscordUserResponse>("/users/@me", accessToken);
    var discordId = "";
    var discordHandle = "";
    var discordEmail = "";
    if (userResponse != null)
    {
        discordId = userResponse.Id;
        discordEmail = userResponse.Email;
        
        
        // depending on the type of discord account this needs to be handled uniquely
        if (userResponse.Discriminator == "0")
        {
            discordHandle = userResponse.Username;
        }
        else
        {
            discordHandle = $"{userResponse.Username}#{userResponse.Discriminator}";
        }
    }
    else
    {
        return Results.Text("Failed to get information");
    }
    
    // get guild information
    var guildResponse = await DiscordClient.Get<DiscordGuild[]>("/users/@me/guilds", accessToken);
    var inGuild = false;
    if (guildResponse != null && guildResponse.Length > 0)
    {
        foreach (var guild in guildResponse)
        {
            if (_DiscordConfig.TargetServers.Contains(guild.Id))
            {
                inGuild = true;
                break;
            }
        }
    }
    else
    {
        inGuild = false;
    }

    var guildNicknames = new Dictionary<string, string>();
    var isAdmin = false;
    var isModerator = false;
    foreach (var guild in _DiscordConfig.TargetServers)
    {
        var guildMemberResponse = await DiscordClient.Get<DiscordGuildMember>($"/users/@me/guilds/${guild}", accessToken);
        if (guildMemberResponse == null)
        {
            LoggerGlobal.Write($"Failed to get information for {guild}");
            continue;
        }
        
        LoggerGlobal.Write($"{JsonSerializer.Serialize(guildMemberResponse)}");
        
        guildNicknames.Add(guild, guildMemberResponse.Nickname ?? discordHandle);
        
        foreach (var role in guildMemberResponse.Roles)
        {
            if (_DiscordConfig.AdminRoles.Contains(role))
            {
                isAdmin = true;
            }

            if (_DiscordConfig.ModeratorRoles.Contains(role))
            {
                isModerator = true;
            }
        }
            
    }



    httpRequest.HttpContext.Session.SetString("Discord.DiscordID", discordId);
    httpRequest.HttpContext.Session.SetInt32("Discord.InServer", inGuild ? 1 : 0);
    httpRequest.HttpContext.Session.SetString("Discord.DiscordHandle", discordHandle);
    httpRequest.HttpContext.Session.SetString("Discord.Email", discordEmail);
    httpRequest.HttpContext.Session.SetInt32($"Discord.IsAdmin", isAdmin ? 1 : 0);
    httpRequest.HttpContext.Session.SetInt32($"Discord.IsModerator", isModerator ? 1 : 0);
    foreach (var (guild, nickname) in guildNicknames)
    {
        httpRequest.HttpContext.Session.SetString($"Discord.Guild.{guild}.Nickname", nickname);
    }
    
    
    var html =
        $"<!DOCTYPE html><html><head><title>Auth | Level Crush</title></head><body><p>Validated. You can close this window now.</p><script>window.close();</script></body><html>";
    
    httpRequest.HttpContext.Response.ContentType = "text/html";
    httpRequest.HttpContext.Response.ContentLength = Encoding.UTF8.GetByteCount(html);

    return Results.Content(html);
});

app.MapGet("/platform/discord/session", (HttpRequest httpRequest) =>
{
    var discordId = httpRequest.HttpContext.Session.GetString("Discord.DiscordID");
    var inServer = httpRequest.HttpContext.Session.GetInt32("Discord.InServer") == 1 ? true : false;
    var discordHandle =  httpRequest.HttpContext.Session.GetString("Discord.DiscordHandle");
    var discordEmail =  httpRequest.HttpContext.Session.GetString("Discord.Email");
    var isAdmin = httpRequest.HttpContext.Session.GetInt32("Discord.IsAdmin") == 1 ? true : false;
    var isModerator = httpRequest.HttpContext.Session.GetInt32("Discord.IsModerator") == 1 ? true : false;

    var nicknames = new List<string>();
    var nicknameKeys = httpRequest.HttpContext.Session.Keys.Where((x) => x.StartsWith("Discord.Guild.") && x.EndsWith("Nickname"));
    foreach (var key  in nicknameKeys)
    {
        nicknames.Add(httpRequest.HttpContext.Session.GetString(key) ?? "@Unknown");
    }
    
    return Results.Json(new DiscordValidationResult()
    {
        Id = discordId ?? "",
        InServer = inServer,
        Handle = discordHandle ?? "",
        Email = discordEmail ?? "",
        IsAdmin = isAdmin,
        IsModerator = isModerator,
        Nicknames = nicknames.ToArray(),
    });

});

app.MapGet("/platform/discord/logout", (HttpRequest httpRequest) =>
{
    var discordKeys = httpRequest.HttpContext.Session.Keys.Where((x) => x.Contains("Discord."));
    foreach (var discordKey in discordKeys)
    {
        httpRequest.HttpContext.Session.Remove(discordKey);
    }
    return Results.Text("200 OK");
});

app.Run();