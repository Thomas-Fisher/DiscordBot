using DiscordBot.Application.Models;
using DiscordBot.Domain.Entities;
using DiscordBot.Application.Errors;

namespace DiscordBot.Application.Interfaces;

/// <summary>
/// Signing people up to events from any source (a reaction, the WoW spec picker, the API). Owns the rules, the activity log
/// entry and the refresh of the Discord message.
/// </summary>
/// <remarks>The value of a successful result is the person's place in the line, for WoW events.</remarks>
public interface ISignupService
{
    /// <summary>
    /// Signs the user up. For WoW events a class and spec can be given (both, or neither); signing up again with a new
    /// class/spec while already signed up changes the spec and keeps the place in the line.
    /// </summary>
    Task<Result<int?>> SignUpAsync(Event evt, ulong userId, string? wowClass, string? wowSpec, ActivityContext context);

    /// <summary>Signs the user off. Fails with <see cref="SignupErrors.NotSignedUp"/> when they had no active signup.</summary>
    Task<Result<int?>> SignOffAsync(Event evt, ulong userId, ActivityContext context);

    Task<Result<int?>> MarkUnsureAsync(Event evt, ulong userId, ActivityContext context);

    Task<Result<int?>> UpdateAsync(Event evt, ulong userId, SignupUpdate update, ActivityContext context);

    /// <summary>
    /// Replaces the raid groups of a WoW event: <paramref name="groups"/> maps each person in the roster to a group (1 to 8, five
    /// people at most in each). Everyone else who is signed up is on standby. Nothing changes when the request breaks a rule.
    /// </summary>
    Task<Result> SetRosterAsync(Event evt, IReadOnlyDictionary<ulong, int> groups, ActivityContext context);

    Task<IReadOnlyList<EventSignup>> GetSignupsAsync(ulong eventId, CancellationToken cancellationToken = default);

    /// <summary>The signed-up and unsure counts for each of the events. Events with no signups are not in the result.</summary>
    Task<IReadOnlyDictionary<ulong, SignupCounts>> GetCountsAsync(IReadOnlyCollection<ulong> eventIds, CancellationToken cancellationToken = default);
}
