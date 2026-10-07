using DiscordBot.Domain.Entities;

namespace DiscordBot.Application.Interfaces;

public interface IEventScheduler
{
    /// <summary>
    /// Completes every recurring event whose time has come and creates its next occurrence in the future (missed occurrences after
    /// downtime are skipped). Returns the new occurrences, which have no Discord announcement yet.
    /// </summary>
    Task<IReadOnlyList<Event>> RollForwardRecurringEventsAsync();

    /// <summary>Events that should have a Discord announcement and do not (posting failed, or the bot went down before it did), soonest first.</summary>
    Task<IReadOnlyList<Event>> GetUnannouncedEventsAsync();

    /// <summary>
    /// Finds the events whose reminder time has come and claims each one, so it is handed out once even with several workers. The
    /// caller sends the reminders.
    /// </summary>
    Task<IReadOnlyList<Event>> ClaimDueRemindersAsync();
}
