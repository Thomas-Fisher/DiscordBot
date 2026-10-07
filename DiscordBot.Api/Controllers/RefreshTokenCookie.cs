namespace DiscordBot.Api.Controllers;

/// <summary>
/// HttpOnly (scripts cannot read it) and SameSite=Strict (other sites cannot make the browser send it), limited to the auth
/// endpoints.
/// </summary>
public static class RefreshTokenCookie
{
    public const string Name = "refresh_token";

    private const string Path = "/api/auth";

    public static void Set(HttpResponse response, string refreshToken, TimeSpan lifetime)
    {
        response.Cookies.Append(Name, refreshToken, Options(response.HttpContext, lifetime));
    }

    public static void Clear(HttpResponse response)
    {
        response.Cookies.Delete(Name, Options(response.HttpContext, null));
    }

    private static CookieOptions Options(HttpContext context, TimeSpan? lifetime)
    {
        return new CookieOptions
        {
            HttpOnly = true,
            // Secure cookies are refused over plain http, which is what local development uses
            Secure = context.Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = Path,
            MaxAge = lifetime,
            IsEssential = true
        };
    }
}
