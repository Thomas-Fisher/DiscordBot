using System.Text.Json;
using DiscordBot.Application.Interfaces;
using DiscordBot.Application.Models;
using DiscordBot.Domain.Common;
using DiscordBot.Domain.Enums;
using DiscordBot.Domain.Entities;
using DiscordBot.Application.Errors;

namespace DiscordBot.Application.Services;

public class EventService : IEventService
{
    private readonly IEventRepository _events;
    private readonly IGuildRepository _guilds;
    private readonly IActivityLogger _activityLogger;
    private readonly IEventMessageUpdater _messageUpdater;
    private readonly IGuildDirectory _directory;
    private readonly TimeProvider _timeProvider;

    public EventService(IEventRepository events, IGuildRepository guilds, IActivityLogger activityLogger, IEventMessageUpdater messageUpdater,
        IGuildDirectory directory, TimeProvider timeProvider)
    {
        _directory = directory;
        _events = events;
        _guilds = guilds;
        _activityLogger = activityLogger;
        _messageUpdater = messageUpdater;
        _timeProvider = timeProvider;
    }

    private DateTime Now => _timeProvider.GetUtcNow().UtcDateTime;

    public async Task<Result<Event>> CreateAsync(NewEventRequest request, ActivityContext context)
    {
        var now = Now;

        var error = FirstRuleBroken(request);
        if (error != null)
        {
            return Result<Event>.Fail(EventErrors.From(error.Value));
        }

        if (!await RolesBelongToAsync(request.GuildId, request.MentionRoleIds))
        {
            return Result<Event>.Fail(EventErrors.UnknownRole);
        }

        var evt = Event.Schedule(
            UniqueId.Next(), request.GuildId, request.CreatedBy, request.Title, request.Description, request.ScheduledTime, request.Type, now,
            request.ReminderMinutes, request.DurationMinutes, request.MentionRoleIds, request.Recurrence);

        // The event references its guild; the bot may have been added since it last synced its guilds
        await _guilds.EnsureExistsAsync(evt.GuildId);
        await _events.CreateEventAsync(evt);
        await LogAsync(ActivityActionType.Created, evt, context,
            new { title = evt.Title, scheduledTime = evt.ScheduledTime, type = evt.Type.ToString(), recurrence = evt.RecurrencePattern?.ToString(), durationMinutes = evt.DurationMinutes, mentionRoleIds = evt.MentionRoleIds });

        return Result<Event>.Ok(evt);
    }

    public Result Validate(NewEventRequest request)
    {
        var error = FirstRuleBroken(request);

        return error == null ? Result.Ok() : Result.Fail(EventErrors.From(error.Value));
    }

    private EventValidationError? FirstRuleBroken(NewEventRequest request)
    {
        return EventRules.ValidateNew(request.Title, request.Description, request.ScheduledTime, request.ReminderMinutes, request.DurationMinutes,
            request.MentionRoleIds, Now);
    }

    public async Task AttachAnnouncementAsync(Event evt, ulong channelId, ulong messageId)
    {
        evt.AttachAnnouncement(channelId, messageId, Now);
        await _events.UpdateEventAsync(evt);
    }

    public async Task MoveAnnouncementAsync(Event evt, ulong channelId, ulong messageId, ActivityContext context)
    {
        var from = evt.ChannelId;

        evt.AttachAnnouncement(channelId, messageId, Now);
        await _events.UpdateEventAsync(evt);
        await LogAsync(ActivityActionType.Updated, evt, context, new { movedFromChannel = from, movedToChannel = channelId });
    }

    public async Task<Result> UpdateAsync(Event evt, EventUpdate update, ActivityContext context)
    {
        if (!await RolesBelongToAsync(evt.GuildId, update.MentionRoleIds))
        {
            return Result.Fail(EventErrors.UnknownRole);
        }

        var error = evt.ApplyChanges(update, Now);
        if (error != null)
        {
            return Result.Fail(EventErrors.From(error.Value));
        }

        await _events.UpdateEventAsync(evt);
        await LogAsync(ActivityActionType.Updated, evt, context,
            new { title = update.Title == null ? null : evt.Title, description = update.Description == null ? null : evt.Description, scheduledTime = update.ScheduledTime, reminderMinutes = update.ReminderMinutes, durationMinutes = update.DurationMinutes, mentionRoleIds = update.MentionRoleIds });

        await RefreshAnnouncementAsync(evt);
        return Result.Ok();
    }

    public async Task<Result> CancelAsync(Event evt, ActivityContext context)
    {
        var error = evt.Cancel(Now);
        if (error != null)
        {
            return Result.Fail(EventErrors.From(error.Value));
        }

        await _events.UpdateEventAsync(evt);
        await LogAsync(ActivityActionType.Cancelled, evt, context, new { status = "Cancelled" });

        await RefreshAnnouncementAsync(evt);
        return Result.Ok();
    }

    public async Task<Result> DeleteAsync(Event evt, ActivityContext context)
    {
        var now = Now;
        if (!evt.MarkDeleted(now))
        {
            return Result.Fail(EventErrors.NotFound);
        }

        await _events.UpdateEventAsync(evt);
        await LogAsync(ActivityActionType.Deleted, evt, context, new { isDeleted = true, deletedAt = now });

        // An event nobody can see any more should not stay announced (and open for reactions) in the channel
        if (evt.ChannelId is { } channelId && evt.MessageId is { } messageId)
        {
            await _messageUpdater.RequestRemovalAsync(channelId, messageId);
        }

        return Result.Ok();
    }

    public async Task<int> CompleteEndedEventsAsync()
    {
        var now = Now;
        var completedCount = 0;

        foreach (var evt in (await _events.GetStartedOneOffEventsAsync(now)).Where(e => e.HasEnded(now)))
        {
            evt.Complete(now);
            await _events.UpdateEventAsync(evt);
            await LogAsync(ActivityActionType.Completed, evt, ActivityContext.System(), new { status = "Completed" });
            completedCount++;
        }

        return completedCount;
    }

    public async Task<int> CancelSeriesAsync(Event evt, ActivityContext context)
    {
        var now = Now;
        var parentId = evt.ParentRecurringEventId ?? evt.EventId;
        var occurrences = await _events.GetEventsByParentIdAsync(parentId);

        var cancelled = occurrences.Where(e => e.CancelAsPartOfSeries(now) == null).ToList();

        // One save for the whole series, so a failure part-way cannot leave it half cancelled
        await _events.UpdateEventsAsync(cancelled);

        foreach (var occurrence in cancelled)
        {
            await RefreshAnnouncementAsync(occurrence);
        }

        var cancelledCount = cancelled.Count;

        await LogAsync(ActivityActionType.CancelledRecurring, evt, context, new { cancelledCount, parentId });

        return cancelledCount;
    }

    private async Task RefreshAnnouncementAsync(Event evt)
    {
        if (evt.MessageId.HasValue)
        {
            await _messageUpdater.RequestRefreshAsync(evt.MessageId.Value);
        }
    }

    public async Task<Event?> FindAsync(ulong eventId, CancellationToken cancellationToken = default)
    {
        var evt = await _events.GetEventByIdAsync(eventId, cancellationToken);

        return evt == null || evt.IsDeleted ? null : evt;
    }

    public Task<IReadOnlyList<Event>> GetActiveAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        return _events.GetActiveEventsAsync(guildId, cancellationToken);
    }

    public Task<GuildEventCounts> GetCountsAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        return _events.GetGuildEventCountsAsync(guildId, cancellationToken);
    }

    private Task LogAsync(ActivityActionType action, Event evt, ActivityContext context, object changes)
    {
        // The title is recorded with every entry so the log can say which event it was, even after the event is renamed or deleted
        var recorded = JsonSerializer.SerializeToNode(changes)!.AsObject();
        recorded["eventTitle"] = evt.Title;

        return _activityLogger.LogAsync(new ActivityEntry(action, ActivityEntity.Event, evt.EventId, evt.GuildId, context.UserId, context.Origin, recorded));
    }

    public Task<Event?> FindByMessageAsync(ulong messageId)
    {
        return _events.GetEventByMessageIdAsync(messageId);
    }

    public Task<IReadOnlyList<Event>> GetUpcomingAsync(ulong guildId)
    {
        return _events.GetUpcomingEventsAsync(guildId);
    }

    // Role ids from another server would ping nobody (or the wrong people): only roles of the event's own server are accepted.
    // When the bot cannot see the server the roles cannot be checked, and are accepted as given.
    private async Task<bool> RolesBelongToAsync(ulong guildId, IReadOnlyCollection<ulong>? roleIds)
    {
        if (roleIds == null || roleIds.Count == 0 || await _directory.GetPingableRolesAsync(guildId) is not { } roles)
        {
            return true;
        }

        var known = roles.Select(r => r.Id).ToHashSet();

        return roleIds.All(known.Contains);
    }
}
