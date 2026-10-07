using DiscordBot.Application.Interfaces;
using DiscordBot.Domain.Entities;
using DiscordBot.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace DiscordBot.Infrastructure.Persistence.Repositories;

public class RememberedSpecRepository : IRememberedSpecRepository
{
    private readonly AppDbContext _context;

    public RememberedSpecRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<string?> FindAsync(ulong guildId, ulong userId, EventType eventType, string wowClass)
    {
        return await Matching(guildId, userId, eventType, wowClass)
            .AsNoTracking()
            .Select(r => r.WowSpec)
            .FirstOrDefaultAsync();
    }

    public async Task SaveAsync(ulong guildId, ulong userId, EventType eventType, string wowClass, string wowSpec)
    {
        if (await UpdateAsync(guildId, userId, eventType, wowClass, wowSpec))
        {
            return;
        }

        var row = new RememberedSpec
        {
            GuildId = guildId,
            UserId = userId,
            EventType = eventType,
            WowClass = wowClass,
            WowSpec = wowSpec,
            UpdatedAt = DateTime.UtcNow
        };
        _context.RememberedSpecs.Add(row);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // The same member's two choices raced and the other insert won: this one replaces it
            _context.Entry(row).State = EntityState.Detached;
            if (!await UpdateAsync(guildId, userId, eventType, wowClass, wowSpec))
            {
                throw;
            }
        }
    }

    public async Task DeleteAsync(ulong guildId, ulong userId, EventType eventType, string wowClass)
    {
        await Matching(guildId, userId, eventType, wowClass).ExecuteDeleteAsync();
    }

    private async Task<bool> UpdateAsync(ulong guildId, ulong userId, EventType eventType, string wowClass, string wowSpec)
    {
        var updatedAt = DateTime.UtcNow;
        var rows = await Matching(guildId, userId, eventType, wowClass)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.WowSpec, wowSpec)
                .SetProperty(r => r.UpdatedAt, updatedAt));
        return rows > 0;
    }

    private IQueryable<RememberedSpec> Matching(ulong guildId, ulong userId, EventType eventType, string wowClass)
    {
        return _context.RememberedSpecs.Where(r =>
            r.GuildId == guildId && r.UserId == userId && r.EventType == eventType && r.WowClass == wowClass);
    }
}
