using DiscordBot.Application.Interfaces;
using DiscordBot.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DiscordBot.Infrastructure.Persistence.Repositories;

public class GuildRepository : IGuildRepository
{
    private readonly AppDbContext _context;

    public GuildRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task SaveGuildAsync(Guild guild)
    {
        var existing = await _context.Guilds.FindAsync(guild.GuildId);
        if (existing == null)
        {
            _context.Guilds.Add(guild);
        }
        else
        {
            // Only the name can change; overwriting the rest would reset when the guild was first seen on every restart
            existing.GuildName = guild.GuildName;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
    }

    public async Task EnsureExistsAsync(ulong guildId)
    {
        if (await _context.Guilds.AnyAsync(g => g.GuildId == guildId))
        {
            return;
        }

        var now = DateTime.UtcNow;
        var guild = new Guild { GuildId = guildId, CreatedAt = now, UpdatedAt = now };
        _context.Guilds.Add(guild);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Another request may have added the same guild between the check and the insert; anything else is a real failure
            _context.Entry(guild).State = EntityState.Detached;
            if (!await _context.Guilds.AnyAsync(g => g.GuildId == guildId))
            {
                throw;
            }
        }
    }

    public async Task<IReadOnlyDictionary<string, string>> GetGuildSettingsAsync(ulong guildId)
    {
        return await _context.GuildSettings
            .AsNoTracking()
            .Where(s => s.GuildId == guildId)
            .ToDictionaryAsync(s => s.SettingKey, s => s.SettingValue);
    }

    public async Task SaveSettingAsync(GuildSettings setting)
    {
        // A setting belongs to a guild row, which may not exist yet (for example a guild the bot has not synced)
        await EnsureExistsAsync(setting.GuildId);

        var existing = await _context.GuildSettings
            .FirstOrDefaultAsync(s => s.GuildId == setting.GuildId && s.SettingKey == setting.SettingKey);

        if (existing == null)
        {
            _context.GuildSettings.Add(setting);
        }
        else
        {
            existing.SettingValue = setting.SettingValue;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
    }
}
