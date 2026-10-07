using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using DiscordBot.Application.Interfaces;
using DiscordBot.Application.Models;
using DiscordBot.Infrastructure.Auth;
using DiscordBot.Infrastructure.Persistence.Repositories;
using DiscordBot.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiscordBot.Tests.Unit.Infrastructure;

public class TokenServiceTests
{
    private static readonly DiscordUser User = new("123456789", "TestUser", "avatarhash");

    private readonly FixedTimeProvider _time = new();
    private readonly TestDb _db = new();
    private readonly TokenService _service;

    public TokenServiceTests()
    {
        var options = TestJwt.Options();

        _service = new TokenService(new RefreshTokenRepository(_db.Context), options, _time, NullLogger<TokenService>.Instance);
    }

    [Fact]
    public async Task Issue_DiscordUser_ReturnsAnAccessTokenWithTheUsersClaimsAndNoGuildClaims()
    {
        var pair = await _service.IssueAsync(User);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(pair.AccessToken);
        Assert.Equal("123456789", jwt.Claims.First(c => c.Type == "discord_user_id").Value);
        Assert.Equal("TestUser", jwt.Claims.First(c => c.Type == "discord_username").Value);
        Assert.Equal("avatarhash", jwt.Claims.First(c => c.Type == "avatar").Value);
        Assert.DoesNotContain(jwt.Claims, c => c.Type.StartsWith("guild_"));
        Assert.Equal(_time.UtcNow.AddMinutes(15), jwt.ValidTo, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Issue_DiscordUser_StoresOnlyTheHashOfTheRefreshToken()
    {
        var pair = await _service.IssueAsync(User);

        using var context = _db.Open();
        var stored = await context.RefreshTokens.SingleAsync();
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pair.RefreshToken))), stored.TokenHash);
        Assert.Equal(_time.UtcNow.AddDays(7), stored.ExpiresAt);
    }

    [Fact]
    public async Task Issue_LongAfterAnEarlierTokenExpired_RemovesTheExpiredToken()
    {
        await _service.IssueAsync(User);
        _time.Advance(TimeSpan.FromDays(10));

        await _service.IssueAsync(User);

        using var context = _db.Open();
        Assert.Equal(1, await context.RefreshTokens.CountAsync());
    }

    [Fact]
    public async Task Refresh_ValidToken_IssuesANewTokenInTheSameSessionAndRevokesTheOldOne()
    {
        var first = await _service.IssueAsync(User);
        _time.Advance(TimeSpan.FromMinutes(20));

        var second = (await _service.RefreshAsync(first.RefreshToken)).Tokens;

        Assert.NotNull(second);
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);
        Assert.Equal(("123456789", "avatarhash"), (second.UserId, second.Avatar));
        using var context = _db.Open();
        Assert.Equal(2, await context.RefreshTokens.CountAsync());
        Assert.Equal(1, await context.RefreshTokens.CountAsync(t => t.RevokedAt == null));
        Assert.Single(await context.RefreshTokens.Select(t => t.FamilyId).Distinct().ToListAsync());
    }

    [Fact]
    public async Task Refresh_UnknownToken_ReturnsNull()
    {
        var result = (await _service.RefreshAsync("not-a-real-token")).Tokens;

        Assert.Null(result);
    }

    [Fact]
    public async Task Refresh_ExpiredToken_ReturnsNull()
    {
        var pair = await _service.IssueAsync(User);
        _time.Advance(TimeSpan.FromDays(8));

        var result = (await _service.RefreshAsync(pair.RefreshToken)).Tokens;

        Assert.Null(result);
    }

    [Fact]
    public async Task Refresh_RotatedTokenReplayedLater_EndsTheWholeSession()
    {
        var first = await _service.IssueAsync(User);
        var second = (await _service.RefreshAsync(first.RefreshToken)).Tokens;
        _time.Advance(TimeSpan.FromMinutes(5));

        var replay = (await _service.RefreshAsync(first.RefreshToken)).Tokens;
        var newestAfterReplay = (await _service.RefreshAsync(second!.RefreshToken)).Tokens;

        // A stolen, already-used token also kills the legitimate holder's newest token
        Assert.Null(replay);
        Assert.Null(newestAfterReplay);
    }

    [Fact]
    public async Task Refresh_RotatedTokenReusedWithinTheGracePeriod_FailsButTheSessionSurvives()
    {
        var first = await _service.IssueAsync(User);
        var second = (await _service.RefreshAsync(first.RefreshToken)).Tokens;
        _time.Advance(TimeSpan.FromSeconds(3));

        var racing = await _service.RefreshAsync(first.RefreshToken);
        var newest = (await _service.RefreshAsync(second!.RefreshToken)).Tokens;

        Assert.Null(racing.Tokens);
        Assert.False(racing.SessionEnded);
        Assert.NotNull(newest);
    }

    [Fact]
    public async Task Refresh_UnknownOrReplayedToken_SaysTheSessionHasEnded()
    {
        var first = await _service.IssueAsync(User);
        await _service.RefreshAsync(first.RefreshToken);
        _time.Advance(TimeSpan.FromMinutes(5));

        var unknown = await _service.RefreshAsync("not-a-real-token");
        var replay = await _service.RefreshAsync(first.RefreshToken);

        Assert.True(unknown.SessionEnded);
        Assert.True(replay.SessionEnded);
    }

    // Lets a second request win the race: just before this request's save, another context revokes the same token
    private sealed class RivalRefreshInterceptor : SaveChangesInterceptor
    {
        private readonly Func<Task> _rival;
        private bool _done;

        public RivalRefreshInterceptor(Func<Task> rival)
        {
            _rival = rival;
        }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (!_done)
            {
                _done = true;
                await _rival();
            }

            return result;
        }
    }

    [Fact]
    public async Task Refresh_AnotherRequestRevokesTheTokenBetweenReadAndSave_ExactlyOneRequestContinuesTheSession()
    {
        var first = await _service.IssueAsync(User);
        var options = TestJwt.Options();

        var rival = new TokenService(new RefreshTokenRepository(_db.Open()), options, _time, NullLogger<TokenService>.Instance);
        RefreshResult? rivalResult = null;
        var interceptor = new RivalRefreshInterceptor(async () => rivalResult = await rival.RefreshAsync(first.RefreshToken));
        var loserContext = new DiscordBot.Infrastructure.Persistence.AppDbContext(
            new DbContextOptionsBuilder<DiscordBot.Infrastructure.Persistence.AppDbContext>().UseNpgsql(_db.ConnectionString).AddInterceptors(interceptor).Options);
        var loser = new TokenService(new RefreshTokenRepository(loserContext), options, _time, NullLogger<TokenService>.Instance);

        var result = await loser.RefreshAsync(first.RefreshToken);

        Assert.NotNull(rivalResult!.Tokens);
        Assert.Null(result.Tokens);
        Assert.False(result.SessionEnded);
        await using var check = _db.Open();
        Assert.Equal(1, await check.RefreshTokens.CountAsync(t => t.UserId == 123456789UL && t.RevokedAt == null));
    }

    [Fact]
    public async Task Revoke_CurrentToken_EndsTheSession()
    {
        var first = await _service.IssueAsync(User);
        var second = (await _service.RefreshAsync(first.RefreshToken)).Tokens;
        await _service.RevokeAsync(second!.RefreshToken);
        _time.Advance(TimeSpan.FromMinutes(1));

        var result = (await _service.RefreshAsync(second.RefreshToken)).Tokens;

        Assert.Null(result);
    }
}
