using DiscordBot.Application.Interfaces;
using DiscordBot.Application.Models;
using DiscordBot.Domain.Common;
using DiscordBot.Domain.Entities;
using DiscordBot.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Application.Services;

public class EventScheduler : IEventScheduler
{
    // Reminders can be set up to this far ahead, so only events starting within this window can be due
    private static readonly TimeSpan ReminderLookahead = TimeSpan.FromMinutes(EventRules.MaxReminderMinutes);

    private const int MaxUnannouncedPerPass = 25;

    private readonly IEventRepository _events;
    private readonly ISettingsService _settings;
    private readonly IActivityLogger _activityLogger;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EventScheduler> _logger;

    public EventScheduler(IEventRepository events, ISettingsService settings, IActivityLogger activityLogger, TimeProvider timeProvider, ILogger<EventScheduler> logger)
    {
        _events = events;
        _settings = settings;
        _activityLogger = activityLogger;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    private DateTime Now => _timeProvider.GetUtcNow().UtcDateTime;

    public async Task<IReadOnlyList<Event>> RollForwardRecurringEventsAsync()
    {
        var created = new List<Event>();

        foreach (var evt in await _events.GetDueRecurringEventsAsync())
        {
            try
            {
                if (await RollForwardAsync(evt) is { } next)
                {
                    created.Add(next);
                }
            }
            catch (Exception ex)
            {
                // One broken series must not stop the others from rolling forward
                _logger.LogError(ex, "Error creating recurring event instance for parent {ParentId}", evt.EventId);
            }
        }

        return created;
    }

    public Task<IReadOnlyList<Event>> GetUnannouncedEventsAsync()
    {
        return _events.GetUnannouncedEventsAsync(Now, MaxUnannouncedPerPass);
    }

    public async Task<IReadOnlyList<Event>> ClaimDueRemindersAsync()
    {
        var now = Now;
        var claimed = new List<Event>();

        foreach (var evt in await _events.GetEventsDueForReminderAsync(now, ReminderLookahead))
        {
            // Claiming marks the reminder as sent before anything is sent: if the worker dies part-way, people miss a reminder
            // rather than getting it twice
            if (evt.ReminderMinutesBefore is { } minutes && now >= evt.ScheduledTime.AddMinutes(-minutes) && await _events.TryClaimReminderAsync(evt))
            {
                claimed.Add(evt);
            }
        }

        return claimed;
    }

    private async Task<Event?> RollForwardAsync(Event evt)
    {
        // The series' first occurrence fixes the day of the month for monthly events
        var parentId = evt.ParentRecurringEventId ?? evt.EventId;
        var anchor = parentId == evt.EventId ? evt : await _events.GetEventByIdAsync(parentId);

        var now = Now;
        var zone = await _settings.GetTimeZoneAsync(evt.GuildId);
        var nextScheduledTime = RecurrenceCalculator.NextAfter(evt.ScheduledTime, evt.RecurrencePattern, now, zone, anchor?.ScheduledTime);
        var next = evt.CreateNextOccurrence(UniqueId.Next(), nextScheduledTime, now);

        if (!await _events.CompleteRecurringEventAndCreateNextAsync(evt, next))
        {
            _logger.LogDebug("Recurring event {EventId} was already rolled forward by another worker", evt.EventId);
            return null;
        }

        await _activityLogger.LogAsync(new ActivityEntry(ActivityActionType.CreatedRecurring, ActivityEntity.Event, next.EventId, evt.GuildId, evt.CreatedBy,
            ActivityOrigin.Recurring, new { eventTitle = evt.Title, parentEventId = next.ParentRecurringEventId, nextScheduledTime, pattern = evt.RecurrencePattern }));

        _logger.LogInformation("Created new instance of recurring event {ParentId}: {EventId} scheduled for {Time}", next.ParentRecurringEventId, next.EventId, nextScheduledTime);
        return next;
    }
}
