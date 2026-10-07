using DiscordBot.Domain.Enums;

namespace DiscordBot.Application.Models;

public record NewEventRequest(
    ulong GuildId,
    ulong CreatedBy,
    string Title,
    string Description,
    DateTime ScheduledTime,
    EventType Type,
    int? ReminderMinutes = null,
    RecurrencePattern? Recurrence = null,
    int? DurationMinutes = null,
    IReadOnlyCollection<ulong>? MentionRoleIds = null);
