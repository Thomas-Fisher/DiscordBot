using DiscordBot.Domain.Enums;

namespace DiscordBot.Discord.Services;

/// <summary>An event whose first form has been filled in and that is waiting for the optional duration and roles step.</summary>
public record PendingEvent(string Title, string Description, DateTime ScheduledTime, EventType EventType, int? ReminderMinutes, RecurrencePattern? Recurrence);
