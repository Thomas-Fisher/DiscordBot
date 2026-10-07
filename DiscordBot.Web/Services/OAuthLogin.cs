using System.Security.Cryptography;

namespace DiscordBot.Web.Services;

/// <summary>
/// The browser side of the Discord login. A random <c>state</c> is remembered before leaving and must come back unchanged, so a
/// link carrying someone else's authorisation code cannot sign this browser in as them.
/// </summary>
public static class OAuthLogin
{
    public const string StateStorageKey = "oauth_state";

    private const string Scope = "identify guilds";

    public static string NewState()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static string AuthorizeUrl(string clientId, string redirectUri, string state)
    {
        return "https://discord.com/api/oauth2/authorize"
            + $"?client_id={Uri.EscapeDataString(clientId)}"
            + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}"
            + "&response_type=code"
            + $"&scope={Uri.EscapeDataString(Scope)}"
            + $"&state={Uri.EscapeDataString(state)}";
    }

    public static bool StateMatches(string? stored, string? returned)
    {
        return !string.IsNullOrEmpty(stored) && !string.IsNullOrEmpty(returned) && stored == returned;
    }
}
