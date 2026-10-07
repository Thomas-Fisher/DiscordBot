using DiscordBot.Application.Interfaces;
using DiscordBot.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Infrastructure.Persistence.Repositories;

public class MessageLogRepository : IMessageLogRepository
{
    private readonly AppDbContext _context;
    private readonly ILogger<MessageLogRepository> _logger;

    public MessageLogRepository(AppDbContext context, ILogger<MessageLogRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task LogMessagesAsync(IReadOnlyCollection<MessageLog> messages)
    {
        if (messages.Count == 0)
        {
            return;
        }

        try
        {
            await StoreAsync(messages);
        }
        catch (DbUpdateException ex)
        {
            // Most likely the same message stored by a concurrent batch; the second attempt re-reads what exists
            _logger.LogWarning(ex, "Storing {Count} message logs failed, retrying once", messages.Count);
            _context.ChangeTracker.Clear();
            await StoreAsync(messages);
        }
    }

    private async Task StoreAsync(IReadOnlyCollection<MessageLog> messages)
    {
        // Message logs reference their guild, so make sure every guild in the batch exists (one query, not one per message)
        var guildIds = messages.Select(m => m.GuildId).Distinct().ToList();
        var existingGuildIds = await _context.Guilds
            .Where(g => guildIds.Contains(g.GuildId))
            .Select(g => g.GuildId)
            .ToListAsync();

        foreach (var guildId in guildIds.Except(existingGuildIds))
        {
            // Several batches (or the guild sync at startup) can meet the same new guild at once; letting the database
            // ignore a duplicate is race-free, unlike checking first and inserting after
            var now = DateTime.UtcNow;
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO \"Guilds\" (\"GuildId\", \"CreatedAt\", \"UpdatedAt\") VALUES ({(decimal)guildId}, {now}, {now}) ON CONFLICT (\"GuildId\") DO NOTHING");
        }

        // Skip messages that were already stored (e.g. an event delivered twice)
        var messageIds = messages.Select(m => m.MessageId).Distinct().ToList();
        var alreadyStored = (await _context.MessageLogs
            .Where(m => messageIds.Contains(m.MessageId))
            .Select(m => m.MessageId)
            .ToListAsync()).ToHashSet();

        var newMessages = messages
            .Where(m => !alreadyStored.Contains(m.MessageId))
            .GroupBy(m => m.MessageId)
            .Select(g => g.First());

        _context.MessageLogs.AddRange(newMessages);
        await _context.SaveChangesAsync();
    }

    public async Task<MessageLog?> FindAsync(ulong messageId)
    {
        return await _context.MessageLogs.AsNoTracking().FirstOrDefaultAsync(m => m.MessageId == messageId);
    }

    public async Task MarkDeletedAsync(ulong messageId)
    {
        var message = await _context.MessageLogs.FirstOrDefaultAsync(m => m.MessageId == messageId);
        if (message == null || message.IsDeleted)
        {
            return;
        }

        message.IsDeleted = true;
        await _context.SaveChangesAsync();
    }

    public async Task DeleteOldMessagesAsync(int retentionDays)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-retentionDays);
        var deleted = await BatchedDelete.OlderThanAsync(_context, "MessageLogs", "LoggedAt", cutoffDate);

        _logger.LogInformation("Deleted {Count} old message logs", deleted);
    }
}
