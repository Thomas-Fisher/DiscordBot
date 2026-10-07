using DiscordBot.Domain.Common;
using DiscordBot.Application.Interfaces;
using DiscordBot.Application.Models;
using DiscordBot.Domain.Entities;
using DiscordBot.Domain.Events;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace DiscordBot.Infrastructure.Logging;

public class ActivityLogger : IActivityLogger
{
    private readonly IActivityLogRepository _activityLogs;
    private readonly IOutbox _outbox;
    private readonly ILogger<ActivityLogger> _logger;

    public ActivityLogger(IActivityLogRepository activityLogs, IOutbox outbox, ILogger<ActivityLogger> logger)
    {
        _activityLogs = activityLogs;
        _outbox = outbox;
        _logger = logger;
    }

    public async Task LogAsync(ActivityEntry entry)
    {
        try
        {
            var changesJson = entry.Changes != null ? JsonSerializer.Serialize(entry.Changes) : null;

            var activityLog = new ActivityLog
            {
                LogId = UniqueId.Next(),
                Timestamp = DateTime.UtcNow,
                GuildId = entry.GuildId,
                UserId = entry.UserId,
                ActionType = entry.Action.ToString(),
                EntityType = entry.Entity.ToString(),
                EntityId = entry.EntityId,
                Source = entry.Origin.ToString(),
                Changes = changesJson
            };

            // The announcement is stored in the same save as the row it describes
            if (entry.GuildId is { } guildId)
            {
                _outbox.Stage(new ActivityLogCreatedEvent
                {
                    ActionType = entry.Action.ToString(),
                    EntityType = entry.Entity.ToString(),
                    EntityId = entry.EntityId ?? 0,
                    GuildId = guildId,
                    UserId = entry.UserId,
                    Source = entry.Origin.ToString(),
                    ChangesJson = changesJson
                });
            }

            await _activityLogs.CreateActivityLogAsync(activityLog);

            _logger.LogInformation("Activity: {ActionType} on {EntityType} (ID: {EntityId}), Guild: {GuildId}, User: {UserId}, Origin: {Origin}",
                entry.Action, entry.Entity, entry.EntityId, entry.GuildId, entry.UserId, entry.Origin);
        }
        catch (Exception ex)
        {
            _outbox.DiscardStaged();
            _logger.LogError(ex, "Error logging activity: {ActionType} on {EntityType}", entry.Action, entry.Entity);
        }
    }
}
