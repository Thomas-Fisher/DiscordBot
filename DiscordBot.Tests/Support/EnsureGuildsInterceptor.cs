using DiscordBot.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace DiscordBot.Tests.Support;

/// <summary>
/// The database insists that a guild row exists before anything that belongs to the guild is saved. Most tests are about something
/// else, so this creates the guild row for them, the way the application does when the bot first sees a guild.
/// </summary>
public sealed class EnsureGuildsInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is { } context)
        {
            foreach (var guildId in MissingGuildIds(context))
            {
                if (!context.Set<Guild>().AsNoTracking().Any(g => g.GuildId == guildId))
                {
                    context.Set<Guild>().Add(NewGuild(guildId));
                }
            }
        }

        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
        {
            foreach (var guildId in MissingGuildIds(context))
            {
                if (!await context.Set<Guild>().AsNoTracking().AnyAsync(g => g.GuildId == guildId, cancellationToken))
                {
                    context.Set<Guild>().Add(NewGuild(guildId));
                }
            }
        }

        return result;
    }

    private static List<ulong> MissingGuildIds(DbContext context)
    {
        var tracked = context.ChangeTracker.Entries<Guild>().Select(e => e.Entity.GuildId).ToHashSet();

        return context.ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity switch
            {
                Event evt => evt.GuildId,
                ActivityLog { GuildId: { } id } => id,
                MessageLog log => log.GuildId,
                GuildSettings settings => settings.GuildId,
                _ => 0UL
            })
            .Where(id => id != 0 && !tracked.Contains(id))
            .Distinct()
            .ToList();
    }

    private static Guild NewGuild(ulong guildId)
    {
        return new Guild { GuildId = guildId, GuildName = "Test guild", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
    }
}
