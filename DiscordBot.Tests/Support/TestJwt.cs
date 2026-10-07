using DiscordBot.Application.Options;
using Microsoft.Extensions.Options;

namespace DiscordBot.Tests.Support;

public static class TestJwt
{
    public const string Issuer = "test-issuer";

    public const string Audience = "test-audience";

    public static IOptions<JwtOptions> Options() => Microsoft.Extensions.Options.Options.Create(new JwtOptions
    {
        Secret = "unit-test-secret-that-is-long-enough-1234567890",
        Issuer = Issuer,
        Audience = Audience,
        ExpiryMinutes = 15,
        RefreshExpiryDays = 7
    });
}
