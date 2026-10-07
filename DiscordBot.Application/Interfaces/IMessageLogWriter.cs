using DiscordBot.Domain.Entities;

namespace DiscordBot.Application.Interfaces;

/// <summary>Accepts message logs without waiting for the database. Implementations write them in batches.</summary>
public interface IMessageLogWriter
{
    /// <summary>Queues the message for storage. Returns false (and drops it) when the buffer is full.</summary>
    bool Enqueue(MessageLog message);
}
