using DiscordBot.Application.Interfaces;
using DiscordBot.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DiscordBot.Infrastructure.Persistence.Repositories;

public class RefreshTokenRepository : IRefreshTokenRepository
{
    private const int PurgeBatchSize = 500;

    private readonly AppDbContext _context;

    public RefreshTokenRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<RefreshToken?> FindByHashAsync(string tokenHash)
    {
        return await _context.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash);
    }

    public async Task AddSessionAsync(RefreshToken first, DateTime purgeExpiredBefore)
    {
        var expired = await _context.RefreshTokens
            .Where(t => t.ExpiresAt < purgeExpiredBefore)
            .OrderBy(t => t.ExpiresAt)   // the oldest first; also what the warning about unordered paging asks for
            .Take(PurgeBatchSize)
            .ToListAsync();

        _context.RefreshTokens.RemoveRange(expired);
        _context.RefreshTokens.Add(first);
        await _context.SaveChangesAsync();
    }

    public async Task<bool> RotateAsync(RefreshToken used, RefreshToken next, DateTime now)
    {
        used.Revoke(now);
        _context.RefreshTokens.Add(next);

        try
        {
            await _context.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // RevokedAt is a concurrency token: another request revoked this token between our read and our save, so it continues the session
            _context.Entry(used).State = EntityState.Detached;
            _context.Entry(next).State = EntityState.Detached;
            return false;
        }
    }

    public async Task RevokeSessionAsync(Guid familyId, DateTime now)
    {
        var tokens = await _context.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ToListAsync();

        foreach (var token in tokens)
        {
            token.Revoke(now);
        }

        await _context.SaveChangesAsync();
    }
}
