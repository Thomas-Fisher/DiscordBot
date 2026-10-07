using DiscordBot.Application.Models;
using DiscordBot.Domain.Entities;

namespace DiscordBot.Application.Interfaces;

public interface IActivityLogRepository
{
    Task CreateActivityLogAsync(ActivityLog activityLog);

    /// <summary>One page of a guild's activity, newest first, continuing after <paramref name="after"/> (null for the first page).</summary>
    Task<CursorPage<ActivityLog>> GetActivityLogsPageAsync(
        ulong guildId, string? actionType, int days, ActivityLogCursor? after, int pageSize, CancellationToken cancellationToken = default);

    Task DeleteOldActivityLogsAsync(int retentionDays);
}
