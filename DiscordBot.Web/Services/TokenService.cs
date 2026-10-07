namespace DiscordBot.Web.Services;

/// <summary>
/// The access token, in memory only and never in <c>localStorage</c>. After a reload it is gone and <see cref="AuthService"/>
/// gets a new one using the refresh cookie.
/// </summary>
public class TokenService
{
    private string? _accessToken;

    public string? AccessToken => _accessToken;

    public void SetAccessToken(string accessToken)
    {
        _accessToken = accessToken;
    }

    public void Clear()
    {
        _accessToken = null;
    }

    /// <summary>
    /// True when the token's <c>exp</c> claim is in the past (or within <paramref name="skew"/> of now),
    /// or when the token can't be read at all.
    /// </summary>
    public static bool IsTokenExpired(string token, TimeSpan? skew = null)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3)
            {
                return true;
            }

            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');

            using var document = System.Text.Json.JsonDocument.Parse(Convert.FromBase64String(payload));
            if (!document.RootElement.TryGetProperty("exp", out var exp) || !exp.TryGetInt64(out var seconds))
            {
                return true;
            }

            var expiresAt = DateTimeOffset.FromUnixTimeSeconds(seconds);
            return expiresAt <= DateTimeOffset.UtcNow.Add(skew ?? TimeSpan.FromSeconds(30));
        }
        catch (Exception ex) when (ex is FormatException or System.Text.Json.JsonException or ArgumentOutOfRangeException)
        {
            // Not a readable token, so treat it as expired
            return true;
        }
    }
}
