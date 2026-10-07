namespace DiscordBot.Application.Interfaces;

/// <summary>
/// Runs work off the caller's thread (for example the Discord gateway thread) without losing exceptions.
/// Work items run one at a time, in the order they were enqueued.
/// </summary>
public interface IBackgroundTaskQueue
{
    /// <summary>Queues the work item. Returns false (and drops the item) when the queue is full.</summary>
    bool Enqueue(string name, Func<CancellationToken, Task> work);
}
