using DiscordBot.Application.Models;

namespace DiscordBot.Application.Interfaces;

/// <summary>Reading a guild's activity log. Writing to it is <see cref="IActivityLogger"/>.</summary>
public interface IActivityLogService
{
    public const int DefaultDays = 7;
    public const int MaxDays = 90;
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 500;

    /// <summary>
    /// One page, newest first, of the last <paramref name="days"/> days (default and limit above), optionally for one action type.
    /// Values outside the limits are brought inside them.
    /// </summary>
    Task<ActivityLogPage> GetPageAsync(ulong guildId, string? actionType, int? days, ActivityLogCursor? after, int pageSize, CancellationToken cancellationToken = default);
}
