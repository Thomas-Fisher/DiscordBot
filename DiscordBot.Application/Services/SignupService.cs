using DiscordBot.Application.Interfaces;
using DiscordBot.Application.Models;
using DiscordBot.Domain.Common;
using DiscordBot.Domain.Entities;
using DiscordBot.Domain.Enums;
using DiscordBot.Domain.Wow;
using DiscordBot.Application.Errors;

namespace DiscordBot.Application.Services;

public class SignupService : ISignupService
{
    private readonly ISignupRepository _signups;
    private readonly IActivityLogger _activityLogger;
    private readonly IEventMessageUpdater _messageUpdater;
    private readonly TimeProvider _timeProvider;

    public SignupService(ISignupRepository signups, IActivityLogger activityLogger, IEventMessageUpdater messageUpdater, TimeProvider timeProvider)
    {
        _signups = signups;
        _activityLogger = activityLogger;
        _messageUpdater = messageUpdater;
        _timeProvider = timeProvider;
    }

    private bool IsOpen(Event evt)
    {
        return !evt.IsDeleted && evt.Status == EventStatus.Scheduled && !evt.HasEnded(_timeProvider.GetUtcNow().UtcDateTime);
    }

    public async Task<Result<int?>> SignUpAsync(Event evt, ulong userId, string? wowClass, string? wowSpec, ActivityContext context)
    {
        if (!IsOpen(evt))
        {
            return Result<int?>.Fail(SignupErrors.EventNotOpen);
        }

        var selectionError = TryReadSelection(evt, wowClass, wowSpec, out var selectedClass, out var selectedSpec);
        if (selectionError != null)
        {
            return Result<int?>.Fail(selectionError);
        }

        var existing = await _signups.GetEventSignupAsync(evt.EventId, userId);
        if (existing?.Status == SignupStatus.SignedUp && selectedClass == null)
        {
            return Result<int?>.Fail(SignupErrors.AlreadySignedUp);
        }

        var order = await _signups.SignUpAsync(evt.EventId, userId, selectedClass, selectedSpec);

        await LogAsync(ActivityActionType.SignedUp, evt, userId, context, new { wowClass = selectedClass, wowSpec = selectedSpec, order });
        await RefreshAnnouncementAsync(evt);
        return Result<int?>.Ok(order);
    }

    public async Task<Result> SetRosterAsync(Event evt, IReadOnlyDictionary<ulong, int> groups, ActivityContext context)
    {
        if (!evt.IsWow)
        {
            return Result.Fail(SignupErrors.RosterNeedsWowEvent);
        }

        if (!IsOpen(evt))
        {
            return Result.Fail(SignupErrors.EventNotOpen);
        }

        if (groups.Values.Any(group => group < 1 || group > RosterRules.MaxGroups))
        {
            return Result.Fail(SignupErrors.RosterBadGroup);
        }

        if (groups.Values.GroupBy(group => group).Any(g => g.Count() > RosterRules.GroupSize))
        {
            return Result.Fail(SignupErrors.RosterGroupFull);
        }

        if (!await _signups.SetRosterAsync(evt.EventId, groups))
        {
            return Result.Fail(SignupErrors.RosterNotSignedUp);
        }

        await _activityLogger.LogAsync(new ActivityEntry(ActivityActionType.RosterUpdated, ActivityEntity.Event, evt.EventId, evt.GuildId, context.UserId, context.Origin,
            new { eventTitle = evt.Title, people = groups.Count, groups = groups.Values.Distinct().Count() }));
        await RefreshAnnouncementAsync(evt);

        return Result.Ok();
    }

    public async Task<Result<int?>> SignOffAsync(Event evt, ulong userId, ActivityContext context)
    {
        var existing = await _signups.GetEventSignupAsync(evt.EventId, userId);
        if (existing == null || existing.Status == SignupStatus.SignedOff)
        {
            return Result<int?>.Fail(SignupErrors.NotSignedUp);
        }

        // Leaving a WoW raid closes the gap in the numbering
        await _signups.SignOffAsync(evt.EventId, userId, compactOrder: evt.IsWow);

        await LogAsync(ActivityActionType.SignedOff, evt, userId, context, null);
        await RefreshAnnouncementAsync(evt);
        return Result<int?>.Ok(null);
    }

    public async Task<Result<int?>> MarkUnsureAsync(Event evt, ulong userId, ActivityContext context)
    {
        if (!IsOpen(evt))
        {
            return Result<int?>.Fail(SignupErrors.EventNotOpen);
        }

        var existing = await _signups.GetEventSignupAsync(evt.EventId, userId);
        if (existing?.Status == SignupStatus.Unsure)
        {
            return Result<int?>.Fail(SignupErrors.NothingToChange);
        }

        await _signups.MarkUnsureAsync(evt.EventId, userId, compactOrder: evt.IsWow);

        await LogAsync(ActivityActionType.MarkedUnsure, evt, userId, context, null);
        await RefreshAnnouncementAsync(evt);
        return Result<int?>.Ok(null);
    }

    public async Task<Result<int?>> UpdateAsync(Event evt, ulong userId, SignupUpdate update, ActivityContext context)
    {
        var existing = await _signups.GetEventSignupAsync(evt.EventId, userId);
        if (existing == null)
        {
            return Result<int?>.Fail(SignupErrors.NotSignedUp);
        }

        SignupStatus? requestedStatus = null;
        if (!string.IsNullOrWhiteSpace(update.Status))
        {
            if (!Enum.TryParse<SignupStatus>(update.Status.Trim(), ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            {
                return Result<int?>.Fail(SignupErrors.InvalidStatus);
            }

            requestedStatus = parsed;
        }

        var selectionError = TryReadSelection(evt, update.WowClass, update.WowSpec, out var selectedClass, out var selectedSpec);
        if (selectionError != null)
        {
            return Result<int?>.Fail(selectionError);
        }

        if (requestedStatus == null && selectedClass == null)
        {
            return Result<int?>.Fail(SignupErrors.NothingToChange);
        }

        var target = requestedStatus ?? existing.Status;

        if (selectedClass != null && target != SignupStatus.SignedUp)
        {
            return Result<int?>.Fail(SignupErrors.ClassSpecRequiresSignedUp);
        }

        switch (target)
        {
            case SignupStatus.SignedOff:
                return existing.Status == SignupStatus.SignedOff
                    ? Result<int?>.Fail(SignupErrors.NothingToChange)
                    : await SignOffAsync(evt, userId, context);

            case SignupStatus.Unsure:
                return await MarkUnsureAsync(evt, userId, context);

            default:
                if (existing.Status == SignupStatus.SignedUp && selectedClass == null)
                {
                    return Result<int?>.Fail(SignupErrors.NothingToChange);
                }

                return await SignUpAsync(evt, userId, selectedClass, selectedSpec, context);
        }
    }

    /// <summary>
    /// Reads an optional class/spec pair. Both or neither must be given; when given they must be a real WoW class and one of
    /// its specs, on a WoW event. Returns the reason it is not acceptable, or null.
    /// </summary>
    private static Error? TryReadSelection(Event evt, string? wowClass, string? wowSpec, out string? selectedClass, out string? selectedSpec)
    {
        selectedClass = null;
        selectedSpec = null;

        var hasClass = !string.IsNullOrWhiteSpace(wowClass);
        var hasSpec = !string.IsNullOrWhiteSpace(wowSpec);

        if (!hasClass && !hasSpec)
        {
            return null;
        }

        if (hasClass != hasSpec)
        {
            return SignupErrors.ClassAndSpecRequiredTogether;
        }

        if (!evt.IsWow || !WowCatalog.TryNormalize(evt.Type, wowClass, wowSpec, out var normalizedClass, out var normalizedSpec))
        {
            return SignupErrors.InvalidClassOrSpec;
        }

        selectedClass = normalizedClass;
        selectedSpec = normalizedSpec;
        return null;
    }

    private async Task LogAsync(ActivityActionType action, Event evt, ulong userId, ActivityContext context, object? details)
    {
        // The actor is whoever made the change (the reacting user on Discord, an admin through the API);
        // the signup itself may belong to someone else, so the affected user is recorded too
        await _activityLogger.LogAsync(new ActivityEntry(action, ActivityEntity.Event, evt.EventId, evt.GuildId, context.UserId ?? userId, context.Origin,
            new { userId, eventTitle = evt.Title, details }));
    }

    private async Task RefreshAnnouncementAsync(Event evt)
    {
        if (evt.MessageId.HasValue)
        {
            await _messageUpdater.RequestRefreshAsync(evt.MessageId.Value);
        }
    }

    public Task<IReadOnlyList<EventSignup>> GetSignupsAsync(ulong eventId, CancellationToken cancellationToken = default)
    {
        return _signups.GetEventSignupsAsync(eventId, cancellationToken);
    }

    public Task<IReadOnlyDictionary<ulong, SignupCounts>> GetCountsAsync(IReadOnlyCollection<ulong> eventIds, CancellationToken cancellationToken = default)
    {
        return _signups.GetSignupCountsAsync(eventIds, cancellationToken);
    }
}
