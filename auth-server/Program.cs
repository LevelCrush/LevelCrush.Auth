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

var _DiscordValidationResults = new Dictionary<string, (DiscordValidationResult,long)>();
var _BungieValidationResults = new Dictionary<string, (BungieValidationResult, long)>();

var _DiscordSessionState = new Dictionary<string, (SessionState, long)>();
var _BungieSessionState = new Dictionary<string, (SessionState, long)>();


builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyHeader()
            .AllowAnyMethod()
            .SetIsOriginAllowed(origin => true)
            .AllowCredentials();        
    });
});


var app = builder.Build();

app.UseRouting();
app.UseCors();



// Start Bungie Linking
app.MapPost("/platform/bungie/claim", async (HttpRequest httpRequest) =>
{
    var res = await httpRequest.ReadFromJsonAsync<Dictionary<string, string>>();
    if (res == null)
    {
        return Results.Json(new BungieValidationResult());
    }
    res.TryGetValue("token", out var token);
    if (token == null)
    {
        return Results.Json(new BungieValidationResult());
    }
    if (_BungieValidationResults.ContainsKey(token))
    {
        var cpy = JsonSerializer.Deserialize<BungieValidationResult>(
            JsonSerializer.Serialize(_BungieValidationResults[token].Item1));
        _BungieValidationResults.Remove(token);
        return Results.Json(cpy);
    }
    else
    {
        return Results.Json(new BungieValidationResult());
    }
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
    
    var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    
    // technically we can do better here...but for now this works
    var hashResults = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes($"{token}||{timestamp}")));
    var bungieState = hashResults;

    if (_BungieSessionState.ContainsKey(bungieState))
    {
        _BungieSessionState.Remove(bungieState);
    }
    
    _BungieSessionState.Add(bungieState,(new SessionState()
    {
        Token = token,
        State = bungieState,
    }, timestamp));
    
    LoggerGlobal.Write($"Starting Bungie login for session token: {token}");
    
    var authorizeUrl =
        $"https://www.bungie.net/en/OAuth/Authorize?response_type=code&client_id={HttpUtility.UrlEncode(_DestinyConfig.ClientId)}&state={bungieState}&prompt=prompt";
    
    return Results.Redirect(authorizeUrl, false, false);
});

app.MapMethods("/platform/bungie/validate",new [] { HttpMethods.Head }, async (HttpRequest HttpRequest) =>
{
    LoggerGlobal.Write("HEAD met for Bungie Validation login");
    return Results.Text("200 OK. Your browser requested the HTTP HEAD method. This is here to prevent your code from being used twice.");
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
    
    if (oauthError != null && oauthError.Length > 0)
    {
        LoggerGlobal.Write($"There was an error found in the oauth request.\n{oauthError}", LogLevel.Error);
        doProcess = false;
    }
   
    if (oauthCode == null || (oauthCode != null && oauthCode.Length == 0))
    {
        LoggerGlobal.Write($"There was no oauth code found in the request", LogLevel.Error);

        doProcess = false;
    }

    if (!_BungieSessionState.ContainsKey(oauthState))
    {
        doProcess = false;
        LoggerGlobal.Write($"State does not exist: Bungie: {oauthState}");
    }
    var session = _BungieSessionState[oauthState].Item1;

    var sessionState = session.State;
    if (oauthState == null || (oauthState != null && oauthState != sessionState))
    {
        LoggerGlobal.Write($"States are mismatching. Bungie: {oauthState} || Session: {sessionState}", LogLevel.Error);
        oauthState = "";
        doProcess = false;
    }

    
    var token = session.Token;
    if (token == null)
    {
        token = "";
        doProcess = false;
    }


    LoggerGlobal.Write($"Validating login for session token: {token}");

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

        LoggerGlobal.Write($"Requesting membership information for {JsonSerializer.Serialize(validationResponse)}");
        var dReq = await DestinyMember.MembershipById(validationResponse.MembershipId);
        
        if (dReq != null)
        {
            membershipId = dReq.MembershipId;
            membershipDisplayName = $"{dReq.GlobalDisplayName}#{dReq.GlobalDisplayNameCode.ToString().PadLeft(4, '0')}";
            membershipPlatform = (int)dReq.MembershipType;
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
    
    
    if (_BungieValidationResults.ContainsKey(token))
    {
        _BungieValidationResults.Remove(token);
    }

    _BungieValidationResults.Add(token, (new BungieValidationResult()
    {
        MembershipId = membershipId.ToString() ?? String.Empty,
        InNetworkClan = inClan,
        MembershipType = membershipPlatform,
        DisplayName = membershipDisplayName,
    }, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
    
    
    LoggerGlobal.Write($"Done Logging in via Bungie for Session Token: {token}");
    
    var html =
        $"<!DOCTYPE html><html><head><title>Auth | Level Crush</title></head><body><p>Validated. You can close this window now.</p><script>window.close();</script></body><html>";
    
    httpRequest.HttpContext.Response.ContentType = "text/html";
    httpRequest.HttpContext.Response.ContentLength = Encoding.UTF8.GetByteCount(html);

    return Results.Content(html);

}); 

// End Bungie Linking


// Start Discord Linking

app.MapGet("/platform/discord/login", async (HttpRequest httpReq) =>
{

    httpReq.Query.TryGetValue("token", out var tokenValues);
    var token = tokenValues.FirstOrDefault();
    if (token == null)
    {
        token = "";
    }
    
    httpReq.Query.TryGetValue("redirectUrl", out var redirectValues);
    var redirect = redirectValues.FirstOrDefault();
    if (redirect == null)
    {
        redirect = "";
    }
    
    httpReq.Query.TryGetValue("userRedirect", out var userRedirectValues);
    var userRedirect = userRedirectValues.FirstOrDefault();
    if (userRedirect == null)
    {
        userRedirect = "";
    }
    
    LoggerGlobal.Write($"Starting Discord Login: {token}");
    
    var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    
    // technically we can do better here...but for now this works
    var hashResults = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes($"{token}||{timestamp}")));
    var discordState = hashResults;
    
    if(_DiscordSessionState.ContainsKey(discordState))
    {
        _DiscordSessionState.Remove(discordState);
    }
    
    _DiscordSessionState.Add(discordState, (new SessionState()
    {
        Token = token,
        State = discordState,
        RedirectUrl = redirect,
        UserRedirect = userRedirect,
    }, timestamp));
    
    var scopes = new string[] { "identify", "guilds", "email", "guilds.members.read" };
    
    var authorizeUrl =
        $"https://discord.com/api/oauth2/authorize?response_type=code&client_id={HttpUtility.UrlEncode(_DiscordConfig.ClientId)}&scope={String.Join('+', scopes)}&state={discordState}&redirect_uri={HttpUtility.UrlEncode(_DiscordConfig.RedirectUrl)}&prompt=none";
    return Results.Redirect(authorizeUrl, false, false);
});


app.MapMethods("/platform/discord/validate",new [] { HttpMethods.Head }, async (HttpRequest HttpRequest) =>
{
    LoggerGlobal.Write("HEAD met for Discord Validation login");
    return Results.Text("200 OK. Your browser requested the HTTP HEAD method. This is here to prevent your code from being used");
});

app.MapGet("/platform/discord/validate", async (HttpRequest httpRequest) =>
{
    LoggerGlobal.Write("Validating Discord Session");
    httpRequest.Query.TryGetValue("code", out var oauthCodeValues);
    httpRequest.Query.TryGetValue("error", out var oauthErrorValues);
    httpRequest.Query.TryGetValue("state", out var oauthStateValues);
        
    var oauthCode = oauthCodeValues.FirstOrDefault();
    var oauthError = oauthErrorValues.FirstOrDefault();
    var oauthState = oauthStateValues.FirstOrDefault();
    
    var doProcess = true;
    
    if (oauthError != null && oauthError.Length > 0)
    {
        LoggerGlobal.Write($"There was an error found in the oauth request.\n{oauthError}", LogLevel.Error);
        doProcess = false;
    }
   
    if (oauthCode == null || (oauthCode != null && oauthCode.Length == 0))
    {
        LoggerGlobal.Write($"There was no oauth code found in the request", LogLevel.Error);
        doProcess = false;
    }

    if (!_DiscordSessionState.ContainsKey(oauthState))
    {
        LoggerGlobal.Write($"The discord oauth state does not exist.", LogLevel.Error);
        doProcess = false;
    }

    var session = _DiscordSessionState[oauthState].Item1;
    var sessionState = session.State;
    
    // with the recent changes, this should actually be impossible. But just in case
    if (oauthState == null || (oauthState != null && oauthState != sessionState))
    {
        LoggerGlobal.Write($"States are mismatching. Discord: {oauthState} || Session: {sessionState}", LogLevel.Error);
        doProcess = false;
    }
    
    var token = session.Token;
    if (token == null)
    {
        token = "";
        doProcess = false;
        LoggerGlobal.Write($"No discord token is tied to this session");
    }
    
    LoggerGlobal.Write($"Validating Discord Login tied to session token: {token}");

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
    
    // send using the raw client to avoid any retry logic
    var oauthResponse = await DiscordClient.Client.ExecuteAsync<DiscordValidationResponse>(req);
    
    var accessToken = "";
    if (oauthResponse.IsSuccessful && oauthResponse.Data != null)
    {
        accessToken = oauthResponse.Data.AccessToken;
    }
    else
    {
        LoggerGlobal.Write($"{JsonSerializer.Serialize(oauthResponse)}", LogLevel.Error);

        var errorMessage = "No information could be retrieved";
        if (oauthResponse.Content != null && oauthResponse.Content.Length > 0)
        {
            errorMessage = oauthResponse.Content;
        }
        
        // after 15 minutes, our sessions are cleared out. However discord has its own rate limiting and it may just be that we are globally rate limited
        return Results.Text($"Discord failed to authenticate you. Please close this tab and try to login after 20 minutes and try again. Additional Information Below: <br /><br /><xmp>{errorMessage}</xmp>");
    }

    var userResponse = await DiscordClient.Get<DiscordUserResponse>("/users/@me", accessToken);
    var discordId = "";
    var discordHandle = "";
    var discordEmail = "";
    var discordGlobalName = "";
    if (userResponse != null)
    {
        discordId = userResponse.Id;
        discordEmail = userResponse.Email;
        
        // depending on the type of discord account this needs to be handled uniquely
        if (userResponse.Discriminator == "0")
        {
            discordHandle = userResponse.Username;
            discordGlobalName = userResponse.GlobalName;
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
    var isRetired = false;
    var isBooster = false;
    foreach (var guild in _DiscordConfig.TargetServers)
    {
        var guildMemberResponse = await DiscordClient.Get<DiscordGuildMember>($"/users/@me/guilds/{guild}/member", accessToken);
        if (guildMemberResponse == null)
        {
            LoggerGlobal.Write($"Failed to get information for {guild}", LogLevel.Error);
            LoggerGlobal.Write($"{JsonSerializer.Serialize(guildMemberResponse)}", LogLevel.Error);
            continue;
        }
        
        guildNicknames.Add(guild, guildMemberResponse.Nickname ?? discordGlobalName);

        if (guildMemberResponse.PremiumSince != null)
        {
            isBooster = true;
        }

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

            if (_DiscordConfig.RetiredRoles.Contains(role))
            {
                isRetired = true;
            }
        }
            
    }


    var nicknames = new List<string>();
    foreach (var (guild, nickname) in guildNicknames)
    {
        nicknames.Add(nickname);
    }

    if (_DiscordValidationResults.ContainsKey(token))
    {
        _DiscordValidationResults.Remove(token);
    }
    
    
    var discordValidationResult = new DiscordValidationResult()
    {
        Id = discordId ?? "",
        InServer = inGuild,
        Handle = discordHandle ?? "",
        Email = discordEmail ?? "",
        IsAdmin = isAdmin,
        IsModerator = isModerator,
        Nicknames = nicknames.ToArray(),
        GlobalName = discordGlobalName ?? "",
        IsRetired = isRetired,
        IsBooster = isBooster
    };

    _DiscordValidationResults.Add(token, (discordValidationResult, DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
    
    LoggerGlobal.Write($"Done Logging in via Discord for Session Token: {token}");

    var currentRedirect = session.RedirectUrl;
    if (currentRedirect != null && currentRedirect.Trim().Length > 0)
    {
        return Results.Redirect(currentRedirect, false, false);
    }
    else
    {

        var html =
            $"<!DOCTYPE html><html><head><title>Auth | Level Crush</title></head><body><p>Validated. You can close this window now.</p><script>window.close();</script></body><html>";

        httpRequest.HttpContext.Response.ContentType = "text/html";
        httpRequest.HttpContext.Response.ContentLength = Encoding.UTF8.GetByteCount(html);

        return Results.Content(html);
    }
});

app.MapPost("/platform/discord/claim", async (HttpRequest httpRequest) =>
{
    LoggerGlobal.Write("Claiming Discord Profile");
    string token = "";
    try
    {
        var res = await httpRequest.ReadFromJsonAsync<Dictionary<string, string>>();
        if (res == null)
        {
            return Results.Json(new DiscordValidationResult());
        }

        res.TryGetValue("token", out token);
    }
    catch (Exception ex)
    {
        token = null;
    }

    if (token == null || token.Length == 0)
    {
        LoggerGlobal.Write("No token provided");
        return Results.Json(new DiscordValidationResult());
    }
    if (_DiscordValidationResults.ContainsKey(token))
    {
        LoggerGlobal.Write("Copying Discord Claim");
        var cpy = JsonSerializer.Deserialize<DiscordValidationResult>(
            JsonSerializer.Serialize(_DiscordValidationResults[token].Item1));
        _DiscordValidationResults.Remove(token);
        return Results.Json(cpy);
    }
    else
    {
        LoggerGlobal.Write("Returning empty discord claim result");
        return Results.Json(new DiscordValidationResult());
    }
});

app.Run();