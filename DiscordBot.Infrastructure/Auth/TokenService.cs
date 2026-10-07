using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using DiscordBot.Application.Interfaces;
using DiscordBot.Application.Models;
using DiscordBot.Application.Options;
using DiscordBot.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using DiscordBot.Application;

namespace DiscordBot.Infrastructure.Auth;

public class TokenService : ITokenService
{
    // A second use of a just-rotated token within this window is treated as a race between two tabs, not theft
    private static readonly TimeSpan ReuseGracePeriod = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ExpiredRetention = TimeSpan.FromDays(1);

    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly JwtOptions _jwtOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TokenService> _logger;

    public TokenService(IRefreshTokenRepository refreshTokens, IOptions<JwtOptions> jwtOptions, TimeProvider timeProvider, ILogger<TokenService> logger)
    {
        _refreshTokens = refreshTokens;
        _jwtOptions = jwtOptions.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    private TimeSpan RefreshLifetime => TimeSpan.FromDays(_jwtOptions.RefreshExpiryDays);

    public async Task<TokenPair> IssueAsync(DiscordUser user)
    {
        if (!ulong.TryParse(user.Id, out var userId))
        {
            throw new InvalidOperationException("Discord user id is not a valid snowflake");
        }

        var now = Now();
        var secret = NewSecret();
        var first = RefreshToken.StartSession(userId, user.Username, user.Avatar, Hash(secret), now, RefreshLifetime, TimeSpan.FromDays(_jwtOptions.MaxSessionDays));

        await _refreshTokens.AddSessionAsync(first, purgeExpiredBefore: now - ExpiredRetention);

        _logger.LogInformation("Issued new session for user {UserId}", user.Id);
        return ToPair(first, secret, now);
    }

    public async Task<RefreshResult> RefreshAsync(string refreshToken)
    {
        var now = Now();
        var existing = await _refreshTokens.FindByHashAsync(Hash(refreshToken));

        if (existing == null)
        {
            return RefreshResult.Ended();
        }

        if (existing.RevokedAt is { } revokedAt)
        {
            if (now - revokedAt > ReuseGracePeriod)
            {
                // A token that was already rotated is being replayed: assume it leaked and end the whole session
                _logger.LogWarning("Revoked refresh token reused for user {UserId}, revoking session {FamilyId}", existing.UserId, existing.FamilyId);
                await _refreshTokens.RevokeSessionAsync(existing.FamilyId, now);
                return RefreshResult.Ended();
            }

            // Just rotated by another request from the same browser (two tabs): its newer cookie is the valid one
            return RefreshResult.Superseded();
        }

        if (existing.ExpiresAt <= now)
        {
            return RefreshResult.Ended();
        }

        var secret = NewSecret();
        var next = existing.Rotate(Hash(secret), now, RefreshLifetime);

        if (!await _refreshTokens.RotateAsync(existing, next, now))
        {
            return RefreshResult.Superseded();
        }

        _logger.LogInformation("Refreshed session for user {UserId}", existing.UserId);
        return RefreshResult.Refreshed(ToPair(next, secret, now));
    }

    public async Task RevokeAsync(string refreshToken)
    {
        var existing = await _refreshTokens.FindByHashAsync(Hash(refreshToken));

        if (existing != null)
        {
            await _refreshTokens.RevokeSessionAsync(existing.FamilyId, Now());
            _logger.LogInformation("Revoked session {FamilyId} for user {UserId}", existing.FamilyId, existing.UserId);
        }
    }

    private DateTime Now()
    {
        return _timeProvider.GetUtcNow().UtcDateTime;
    }

    private TokenPair ToPair(RefreshToken stored, string refreshSecret, DateTime now)
    {
        var accessToken = CreateAccessToken(stored.UserId.ToString(), stored.Username, stored.Avatar, now);

        return new TokenPair(accessToken, refreshSecret, _jwtOptions.ExpiryMinutes * 60, stored.UserId.ToString(), stored.Username, stored.Avatar);
    }

    private string CreateAccessToken(string userId, string username, string? avatar, DateTime now)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtOptions.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        // Guild permissions are deliberately not part of the token; see IGuildAccessService
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new(ClaimTypes.Name, username),
            new(AuthClaims.UserId, userId),
            new(AuthClaims.Username, username),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        if (!string.IsNullOrEmpty(avatar))
        {
            claims.Add(new Claim(AuthClaims.Avatar, avatar));
        }

        var token = new JwtSecurityToken(
            issuer: _jwtOptions.Issuer,
            audience: _jwtOptions.Audience,
            claims: claims,
            notBefore: now,
            expires: now.AddMinutes(_jwtOptions.ExpiryMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static string NewSecret()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string Hash(string token)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
