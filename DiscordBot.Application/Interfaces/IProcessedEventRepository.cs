namespace DiscordBot.Application.Interfaces;

/// <summary>
/// Which events each handler has already handled, so a broker redelivery does not repeat work that is not repeat-safe (such as
/// posting to Discord).
/// </summary>
public interface IProcessedEventRepository
{
    Task<bool> HasProcessedAsync(Guid eventId, string handler);

    /// <summary>Records that the handler dealt with the event. Recording it twice is harmless.</summary>
    Task MarkProcessedAsync(Guid eventId, string handler);

    /// <summary>Deletes records older than the given number of days (a broker never redelivers that far back).</summary>
    Task DeleteOlderThanAsync(int days);
}
