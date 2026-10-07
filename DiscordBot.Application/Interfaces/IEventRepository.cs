using DiscordBot.Application.Models;
using DiscordBot.Domain.Entities;

namespace DiscordBot.Application.Interfaces;

public interface IEventRepository
{
    Task CreateEventAsync(Event @event);

    Task UpdateEventAsync(Event @event);

    /// <summary>Saves several changed events in one transaction: all of them, or none.</summary>
    Task UpdateEventsAsync(IReadOnlyCollection<Event> events);

    /// <summary>Scheduled events that have a channel but no announcement message, oldest first (their announcement failed or never happened).</summary>
    Task<IReadOnlyList<Event>> GetUnannouncedEventsAsync(DateTime now, int limit);

    Task<Event?> GetEventByIdAsync(ulong eventId, CancellationToken cancellationToken = default);

    Task<Event?> GetEventByMessageIdAsync(ulong messageId);

    Task<IReadOnlyList<Event>> GetActiveEventsAsync(ulong guildId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Event>> GetUpcomingEventsAsync(ulong guildId);

    Task<IReadOnlyList<Event>> GetEventsDueForReminderAsync(DateTime now, TimeSpan lookahead);

    /// <summary>One-off scheduled events that have started (they may not have ended yet; check <c>Event.HasEnded</c>).</summary>
    Task<IReadOnlyList<Event>> GetStartedOneOffEventsAsync(DateTime now);

    Task<IReadOnlyList<Event>> GetDueRecurringEventsAsync();

    /// <summary>
    /// Marks the occurrence completed and stores the next one in a single save. Returns false, and stores nothing, when another
    /// worker has already changed the occurrence (so two workers cannot both roll a series forward).
    /// </summary>
    Task<bool> CompleteRecurringEventAndCreateNextAsync(Event completed, Event next);

    /// <summary>
    /// Records that the event's reminder is being sent. Returns false when another worker has already claimed it, in which case
    /// the caller must not send it again.
    /// </summary>
    Task<bool> TryClaimReminderAsync(Event evt);

    /// <summary>All occurrences of a recurring series, including the parent.</summary>
    Task<IReadOnlyList<Event>> GetEventsByParentIdAsync(ulong parentEventId);

    Task<GuildEventCounts> GetGuildEventCountsAsync(ulong guildId, CancellationToken cancellationToken = default);
}
