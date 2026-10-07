using DiscordBot.Domain.Entities;

namespace DiscordBot.Application.Interfaces;

public interface IMessageLogRepository
{
    /// <summary>
    /// Stores a batch. Guilds that don't exist yet are created, and messages already stored (or repeated within the
    /// batch) are skipped, so delivering the same message twice is harmless.
    /// </summary>
    Task LogMessagesAsync(IReadOnlyCollection<MessageLog> messages);


    /// <summary>The stored copy of a message (who wrote it and what it said), or null when it was never stored or has been cleaned up.</summary>
    Task<MessageLog?> FindAsync(ulong messageId);

    /// <summary>Flags a stored message as deleted in Discord. Does nothing if the message was never stored.</summary>
    Task MarkDeletedAsync(ulong messageId);

    Task DeleteOldMessagesAsync(int retentionDays);
}
