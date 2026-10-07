using DiscordBot.Web.Services;

namespace DiscordBot.Tests.Unit.Web;

public class OAuthLoginTests
{
    [Fact]
    public void AuthorizeUrl_CarriesTheStateAndAnEscapedRedirect()
    {
        var url = OAuthLogin.AuthorizeUrl("123", "http://localhost:5000/callback", "abc-_");

        Assert.Contains("client_id=123", url);
        Assert.Contains("redirect_uri=http%3A%2F%2Flocalhost%3A5000%2Fcallback", url);
        Assert.Contains("scope=identify%20guilds", url);
        Assert.EndsWith("&state=abc-_", url);
    }

    [Theory]
    [InlineData("abc", "abc", true)]
    [InlineData("abc", "abd", false)]
    [InlineData("abc", null, false)]      // the return trip did not carry a state
    [InlineData(null, "abc", false)]      // this browser never started a login
    [InlineData("", "", false)]
    public void StateMatches_OnlyWhenBothArePresentAndEqual(string? stored, string? returned, bool expected)
    {
        Assert.Equal(expected, OAuthLogin.StateMatches(stored, returned));
    }
}
