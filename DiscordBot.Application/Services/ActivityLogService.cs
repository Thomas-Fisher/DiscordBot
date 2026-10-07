using DiscordBot.Application.Interfaces;
using DiscordBot.Application.Models;

namespace DiscordBot.Application.Services;

public class ActivityLogService : IActivityLogService
{
    private readonly IActivityLogRepository _activityLogs;

    public ActivityLogService(IActivityLogRepository activityLogs)
    {
        _activityLogs = activityLogs;
    }

    public async Task<ActivityLogPage> GetPageAsync(ulong guildId, string? actionType, int? days, ActivityLogCursor? after, int pageSize, CancellationToken cancellationToken = default)
    {
        var windowDays = Math.Clamp(days ?? IActivityLogService.DefaultDays, 1, IActivityLogService.MaxDays);
        var size = Math.Clamp(pageSize, 1, IActivityLogService.MaxPageSize);

        var page = await _activityLogs.GetActivityLogsPageAsync(guildId, actionType, windowDays, after, size, cancellationToken);

        return new ActivityLogPage(page.Items, page.Next, size);
    }
}
