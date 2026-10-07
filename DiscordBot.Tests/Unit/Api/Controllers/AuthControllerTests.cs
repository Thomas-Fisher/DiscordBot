using DiscordBot.Api.Controllers;
using DiscordBot.Application.Interfaces;
using DiscordBot.Application.Models;
using DiscordBot.Contracts;
using DiscordBot.Infrastructure.Auth;
using DiscordBot.Infrastructure.Persistence.Repositories;
using DiscordBot.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DiscordBot.Tests.Unit.Api.Controllers;

public class AuthControllerTests
{
    private const string DiscordToken = "discord_token";

    private static readonly DiscordUser User = new("123456789", "TestUser", "avatarhash");

    private readonly TestDb _db = new();
    private readonly FixedTimeProvider _time = new();
    private readonly Mock<IDiscordOAuthClient> _discord = new();
    private readonly TokenService _tokens;
    private readonly AuthController _controller;

    public AuthControllerTests()
    {
        var jwt = TestJwt.Options();
        _tokens = new TokenService(new RefreshTokenRepository(_db.Context), jwt, _time, NullLogger<TokenService>.Instance);
        _controller = new AuthController(_discord.Object, _tokens, new Mock<IGuildAccessService>().Object, new FakeGuildDirectory(), jwt, NullLogger<AuthController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    private void WithRefreshCookie(string value)
    {
        _controller.ControllerContext.HttpContext.Request.Headers.Cookie = $"{RefreshTokenCookie.Name}={value}";
    }

    private string SetCookieHeader() => _controller.Response.Headers.SetCookie.ToString();

    private void DiscordAccepts(string code, params DiscordGuildMembership[] guilds)
    {
        _discord.Setup(x => x.ExchangeCodeAsync(code, It.IsAny<CancellationToken>())).ReturnsAsync(DiscordToken);
        _discord.Setup(x => x.GetUserAsync(DiscordToken, It.IsAny<CancellationToken>())).ReturnsAsync(User);
        _discord.Setup(x => x.GetGuildsAsync(DiscordToken, It.IsAny<CancellationToken>())).ReturnsAsync(guilds);
    }

    private static string CookieValueFrom(string setCookieHeader) => setCookieHeader.Split(';')[0].Split('=', 2)[1];

    [Fact]
    public async Task DiscordCallback_ValidCode_ReturnsAnAccessTokenAndOnlyTheGuildsTheUserManages()
    {
        DiscordAccepts("code",
            new DiscordGuildMembership("1", "Owned", null, Owner: true, Permissions: 0),
            new DiscordGuildMembership("2", "Managed", null, Owner: false, Permissions: 0x20),
            new DiscordGuildMembership("3", "Just a member", null, Owner: false, Permissions: 0));

        var result = await _controller.DiscordCallback(new OAuthCallbackRequest { Code = "code" });

        var response = Assert.IsType<AuthTokenResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.False(string.IsNullOrEmpty(response.AccessToken));
        Assert.Equal(new[] { "1", "2" }, response.User.Guilds.Select(g => g.Id));
    }

    [Fact]
    public async Task DiscordCallback_ValidCode_SetsAnHttpOnlyStrictRefreshCookieForTheConfiguredLifetime()
    {
        DiscordAccepts("code");

        await _controller.DiscordCallback(new OAuthCallbackRequest { Code = "code" });

        var cookie = SetCookieHeader();
        Assert.StartsWith("refresh_token=", cookie);
        Assert.True(cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase), "the refresh cookie should carry HttpOnly");
        Assert.True(cookie.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase), "the refresh cookie should carry SameSite=Strict");
        Assert.True(cookie.Contains("path=/api/auth", StringComparison.OrdinalIgnoreCase), "the refresh cookie should carry Path=/api/auth");
        Assert.True(cookie.Contains("max-age=604800", StringComparison.OrdinalIgnoreCase), "the refresh cookie should carry Max-Age of seven days");
    }

    [Fact]
    public async Task DiscordCallback_EmptyCode_IsABadRequest()
    {
        var result = await _controller.DiscordCallback(new OAuthCallbackRequest { Code = " " });

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task DiscordCallback_DiscordGivesNoToken_IsABadRequestAndNoCookieIsSet()
    {
        _discord.Setup(x => x.ExchangeCodeAsync("bad", It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        var result = await _controller.DiscordCallback(new OAuthCallbackRequest { Code = "bad" });

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(SetCookieHeader());
    }

    [Fact]
    public async Task RefreshToken_NoCookie_IsUnauthorized()
    {
        var result = await _controller.RefreshToken();

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    [Fact]
    public async Task RefreshToken_CookieThatIsNotAValidSession_IsUnauthorizedAndTheCookieIsCleared()
    {
        WithRefreshCookie("nope");

        var result = await _controller.RefreshToken();

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
        Assert.StartsWith("refresh_token=;", SetCookieHeader());
    }

    [Fact]
    public async Task RefreshToken_CookieJustRotatedByAnotherTab_IsUnauthorizedButTheCookieIsKept()
    {
        // Another request from the same browser has already used this token and been given a newer cookie
        var session = await _tokens.IssueAsync(User);
        await _tokens.RefreshAsync(session.RefreshToken);
        _time.Advance(TimeSpan.FromSeconds(2));
        WithRefreshCookie(session.RefreshToken);

        var result = await _controller.RefreshToken();

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
        Assert.DoesNotContain("refresh_token", SetCookieHeader());
    }

    [Fact]
    public async Task RefreshToken_ValidCookie_ReturnsANewAccessTokenAndRotatesTheCookie()
    {
        var session = await _tokens.IssueAsync(User);
        WithRefreshCookie(session.RefreshToken);

        var result = await _controller.RefreshToken();

        var response = Assert.IsType<AuthTokenResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.False(string.IsNullOrEmpty(response.AccessToken));
        Assert.NotEqual(session.RefreshToken, CookieValueFrom(SetCookieHeader()));
    }

    [Fact]
    public async Task RefreshToken_ValidCookie_NeverPutsTheRefreshTokenInTheResponseBody()
    {
        var session = await _tokens.IssueAsync(User);
        WithRefreshCookie(session.RefreshToken);

        var result = await _controller.RefreshToken();

        var body = System.Text.Json.JsonSerializer.Serialize(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.DoesNotContain(session.RefreshToken, body);
        Assert.DoesNotContain(CookieValueFrom(SetCookieHeader()), body);
    }

    [Fact]
    public async Task Logout_WithACookie_EndsTheSessionAndClearsTheCookie()
    {
        var session = await _tokens.IssueAsync(User);
        WithRefreshCookie(session.RefreshToken);

        var result = await _controller.Logout();

        Assert.IsType<NoContentResult>(result);
        Assert.StartsWith("refresh_token=;", SetCookieHeader());
        Assert.Null((await _tokens.RefreshAsync(session.RefreshToken)).Tokens);
    }
}
