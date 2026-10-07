namespace DiscordBot.Domain.Entities;

/// <summary>
/// A server-side record of an issued refresh token (only its hash). Tokens rotate on every use and share a <see
/// cref="FamilyId"/> per login, so reuse of a rotated one revokes the session.
/// </summary>
public class RefreshToken
{
    private RefreshToken()
    {
    }

    public Guid Id { get; private set; }
    public Guid FamilyId { get; private set; }
    public ulong UserId { get; private set; }
    public string Username { get; private set; } = string.Empty;
    public string? Avatar { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }

    /// <summary>When the login itself ends, however often the token is renewed before then.</summary>
    public DateTime SessionExpiresAt { get; private set; }

    public DateTime? RevokedAt { get; private set; }

    /// <summary>The first token of a new login. The login ends after <c>sessionLifetime</c>.</summary>
    public static RefreshToken StartSession(ulong userId, string username, string? avatar, string tokenHash, DateTime now, TimeSpan lifetime, TimeSpan sessionLifetime)
    {
        return Create(Guid.NewGuid(), userId, username, avatar, tokenHash, now, lifetime, now + sessionLifetime);
    }

    /// <summary>The token that replaces this one: same login and user, new secret, never outliving the login.</summary>
    public RefreshToken Rotate(string newTokenHash, DateTime now, TimeSpan lifetime)
    {
        return Create(FamilyId, UserId, Username, Avatar, newTokenHash, now, lifetime, SessionExpiresAt);
    }

    public void Revoke(DateTime now)
    {
        RevokedAt = now;
    }

    private static RefreshToken Create(Guid familyId, ulong userId, string username, string? avatar, string tokenHash, DateTime now, TimeSpan lifetime, DateTime sessionExpiresAt)
    {
        return new RefreshToken
        {
            Id = Guid.NewGuid(),
            FamilyId = familyId,
            UserId = userId,
            Username = username,
            Avatar = avatar,
            TokenHash = tokenHash,
            CreatedAt = now,
            ExpiresAt = now + lifetime < sessionExpiresAt ? now + lifetime : sessionExpiresAt,
            SessionExpiresAt = sessionExpiresAt
        };
    }
}
