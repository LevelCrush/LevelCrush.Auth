namespace auth_server.Models;

public class SessionState
{
    public string UserRedirect { get; set; } = string.Empty;
    public string RedirectUrl { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
}