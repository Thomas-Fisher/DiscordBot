using System.Text;
using System.Text.Json;
using DiscordBot.Web.Services;

namespace DiscordBot.Tests.Unit.Web;

public class TokenExpiryTests
{
    private static string Jwt(object payload)
    {
        static string B64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var header = B64(Encoding.UTF8.GetBytes("{\"alg\":\"HS256\",\"typ\":\"JWT\"}"));
        var body = B64(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));
        return $"{header}.{body}.signature";
    }

    private static long InSeconds(int seconds) => DateTimeOffset.UtcNow.AddSeconds(seconds).ToUnixTimeSeconds();

    [Theory]
    [InlineData(600, null, false)]     // well in the future
    [InlineData(-60, null, true)]      // already expired
    [InlineData(10, null, true)]       // inside the default 30 second skew: refresh early so it doesn't die in transit
    [InlineData(10, 0, false)]         // the same token with no skew is still valid
    public void IsTokenExpired_TokenExpiringAtAnOffset_IsJudgedAgainstTheSkew(int expiresInSeconds, int? skewSeconds, bool expected)
    {
        var token = Jwt(new { exp = InSeconds(expiresInSeconds) });
        TimeSpan? skew = skewSeconds.HasValue ? TimeSpan.FromSeconds(skewSeconds.Value) : null;

        var expired = TokenService.IsTokenExpired(token, skew);

        Assert.Equal(expected, expired);
    }

    [Fact]
    public void IsTokenExpired_NoExpiryClaim_IsTreatedAsExpired()
    {
        var token = Jwt(new { sub = "123" });

        var expired = TokenService.IsTokenExpired(token);

        Assert.True(expired);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    [InlineData("a.b.c")]
    [InlineData("a.!!!.c")]
    public void IsTokenExpired_MalformedToken_IsTreatedAsExpired(string token)
    {
        var expired = TokenService.IsTokenExpired(token);

        Assert.True(expired);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void IsTokenExpired_PayloadOfAnyBase64PaddingLength_IsReadCorrectly(int padding)
    {
        // The payload length varies so every padding amount, 0 to 2 '=', is exercised
        var token = Jwt(new { exp = InSeconds(600), pad = new string('x', padding) });

        var expired = TokenService.IsTokenExpired(token);

        Assert.False(expired);
    }
}
