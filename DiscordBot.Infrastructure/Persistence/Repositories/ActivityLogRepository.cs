using DiscordBot.Application.Interfaces;
using DiscordBot.Application.Models;
using DiscordBot.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Infrastructure.Persistence.Repositories;

public class ActivityLogRepository : IActivityLogRepository
{
    private readonly AppDbContext _context;
    private readonly ILogger<ActivityLogRepository> _logger;

    public ActivityLogRepository(AppDbContext context, ILogger<ActivityLogRepository> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task CreateActivityLogAsync(ActivityLog activityLog)
    {
        _context.ActivityLogs.Add(activityLog);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch
        {
            // Left in the change tracker as "added", the row would be inserted again (and fail again) by the next save in this scope
            _context.Entry(activityLog).State = EntityState.Detached;
            throw;
        }
    }

    public async Task<CursorPage<ActivityLog>> GetActivityLogsPageAsync(
        ulong guildId, string? actionType, int days, ActivityLogCursor? after, int pageSize, CancellationToken cancellationToken = default)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-days);
        var query = _context.ActivityLogs
            .AsNoTracking()
            .Where(a => a.GuildId == guildId && a.Timestamp >= cutoffDate);

        if (!string.IsNullOrEmpty(actionType))
        {
            query = query.Where(a => a.ActionType == actionType);
        }

        if (after != null)
        {
            // Keyset condition: strictly older than the last row of the previous page (log id breaks timestamp ties)
            var afterTimestamp = after.Timestamp;
            var afterLogId = after.LogId;
            query = query.Where(a => a.Timestamp < afterTimestamp || (a.Timestamp == afterTimestamp && a.LogId < afterLogId));
        }

        // One extra row tells us whether another page exists without a COUNT over the whole range
        var rows = await query
            .OrderByDescending(a => a.Timestamp)
            .ThenByDescending(a => a.LogId)
            .Take(pageSize + 1)
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > pageSize;
        var items = hasMore ? rows.Take(pageSize).ToList() : rows;
        var next = hasMore ? new ActivityLogCursor(items[^1].Timestamp, items[^1].LogId) : null;

        return new CursorPage<ActivityLog>(items, next);
    }

    public async Task DeleteOldActivityLogsAsync(int retentionDays)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-retentionDays);
        var deleted = await BatchedDelete.OlderThanAsync(_context, "ActivityLogs", "Timestamp", cutoffDate);

        _logger.LogInformation("Deleted {Count} old activity logs", deleted);
    }
}
