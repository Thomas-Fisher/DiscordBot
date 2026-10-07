using DiscordBot.Application.Models;
using DiscordBot.Domain.Common;
using DiscordBot.Domain.Entities;
using DiscordBot.Application.Errors;

namespace DiscordBot.Application.Interfaces;

/// <summary>The event lifecycle (validate, change, record) for create/edit/cancel/delete, from Discord or the API. Callers check the user may do it.</summary>
public interface IEventService
{
    /// <summary>
    /// Validates and stores a new event (without a Discord message yet). Once the announcement has been posted, call
    /// <see cref="AttachAnnouncementAsync"/>.
    /// </summary>
    Task<Result<Event>> CreateAsync(NewEventRequest request, ActivityContext context);

    /// <summary>Checks a new event against the rules without saving it, so a multi-step form can report a problem early.</summary>
    Result Validate(NewEventRequest request);

    Task AttachAnnouncementAsync(Event evt, ulong channelId, ulong messageId);

    /// <summary>Points the event at a new announcement message (after it was re-posted in another channel) and records the move.</summary>
    Task MoveAnnouncementAsync(Event evt, ulong channelId, ulong messageId, ActivityContext context);

    /// <summary>Applies the changes that are set, or says why it was rejected (nothing is changed then).</summary>
    Task<Result> UpdateAsync(Event evt, EventUpdate update, ActivityContext context);

    /// <summary>Cancels the event, or says why it cannot be (it is already cancelled or finished).</summary>
    Task<Result> CancelAsync(Event evt, ActivityContext context);

    /// <summary>Soft-deletes the event and removes its announcement from Discord.</summary>
    Task<Result> DeleteAsync(Event evt, ActivityContext context);

    /// <summary>Marks one-off events whose end time has passed as completed. Returns how many were completed.</summary>
    Task<int> CompleteEndedEventsAsync();

    /// <summary>Cancels every scheduled occurrence of a recurring series. Returns how many were cancelled.</summary>
    Task<int> CancelSeriesAsync(Event evt, ActivityContext context);

    /// <summary>The event, or null when there is none or it was deleted.</summary>
    Task<Event?> FindAsync(ulong eventId, CancellationToken cancellationToken = default);

    /// <summary>A guild's events that are still relevant (not deleted), soonest first.</summary>
    Task<IReadOnlyList<Event>> GetActiveAsync(ulong guildId, CancellationToken cancellationToken = default);

    Task<GuildEventCounts> GetCountsAsync(ulong guildId, CancellationToken cancellationToken = default);

    Task<Event?> FindByMessageAsync(ulong messageId);

    /// <summary>A guild's scheduled events that have not started yet, soonest first.</summary>
    Task<IReadOnlyList<Event>> GetUpcomingAsync(ulong guildId);
}
