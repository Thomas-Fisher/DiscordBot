using DiscordBot.Domain.Entities;

namespace DiscordBot.Application.Interfaces;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> FindByHashAsync(string tokenHash);

    /// <summary>Stores the first token of a new session, and clears out tokens that expired before <paramref name="purgeExpiredBefore"/>.</summary>
    Task AddSessionAsync(RefreshToken first, DateTime purgeExpiredBefore);

    /// <summary>
    /// Revokes <paramref name="used"/> and stores <paramref name="next"/> in one save. Returns false, and stores nothing, when
    /// another request has already revoked <paramref name="used"/> (so only one of two racing requests continues the session).
    /// </summary>
    Task<bool> RotateAsync(RefreshToken used, RefreshToken next, DateTime now);

    Task RevokeSessionAsync(Guid familyId, DateTime now);
}
