using DiscordBot.Application.Models;
using DiscordBot.Domain.Entities;

namespace DiscordBot.Application.Interfaces;

/// <summary>Stores and changes event signups. Changes to a signup are atomic per event (see the individual methods).</summary>
public interface ISignupRepository
{
    Task<IReadOnlyList<EventSignup>> GetEventSignupsAsync(ulong eventId, CancellationToken cancellationToken = default);

    Task<EventSignup?> GetEventSignupAsync(ulong eventId, ulong userId);

    /// <summary>
    /// Sets the raid groups: the people listed are put in their group and everyone else on the event is left without one. Applies
    /// nothing and returns false when one of the people listed is neither signed up nor unsure (checked while holding the event's
    /// lock, so a sign-off cannot slip in between).
    /// </summary>
    Task<bool> SetRosterAsync(ulong eventId, IReadOnlyDictionary<ulong, int> groupsByUser);

    /// <summary>The signed-up and unsure counts for each of the events, in one query. Events with no signups are not in the result.</summary>
    Task<IReadOnlyDictionary<ulong, SignupCounts>> GetSignupCountsAsync(IReadOnlyCollection<ulong> eventIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Signs the user up, or updates their class/spec. Whoever ends up with a class and spec (given now, or kept from an
    /// earlier signup) is given a place in the line, assigned atomically, and it is returned; otherwise null.
    /// </summary>
    Task<int?> SignUpAsync(ulong eventId, ulong userId, string? wowClass = null, string? wowSpec = null);

    /// <summary>
    /// Marks the user's signup as signed off. Returns false when they had no signup. When <paramref name="compactOrder"/>
    /// is set the remaining order numbers are renumbered 1, 2, 3... in the same step.
    /// </summary>
    Task<bool> SignOffAsync(ulong eventId, ulong userId, bool compactOrder);

    /// <summary>
    /// Marks the user as unsure, creating a signup if they had none. <paramref name="compactOrder"/> renumbers the
    /// people still signed up, as for <see cref="SignOffAsync"/>.
    /// </summary>
    Task MarkUnsureAsync(ulong eventId, ulong userId, bool compactOrder);
}
