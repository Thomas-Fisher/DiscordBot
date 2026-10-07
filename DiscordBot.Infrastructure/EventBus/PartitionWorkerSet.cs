using System.Collections.Concurrent;
using System.Threading.Channels;
using Confluent.Kafka;

namespace DiscordBot.Infrastructure.EventBus;

/// <summary>
/// Runs work in order per Kafka partition, partitions side by side, so a slow or retrying message holds up only its own
/// partition.
/// </summary>
public sealed class PartitionWorkerSet
{
    private const int QueueCapacity = 100;

    private readonly ConcurrentDictionary<TopicPartition, Worker> _workers = new();
    private volatile Exception? _failure;

    /// <summary>The first unexpected failure of any worker, or null. The owner should treat it as fatal and start again.</summary>
    public Exception? Failure => _failure;

    /// <summary>Queues the work behind whatever that partition is already doing. Waits if the partition has a long backlog.</summary>
    public async ValueTask SubmitAsync(TopicPartition partition, Func<CancellationToken, Task> work, CancellationToken cancellationToken)
    {
        var worker = _workers.GetOrAdd(partition, _ => new Worker(this));
        await worker.Queue.Writer.WriteAsync(work, cancellationToken);
    }

    /// <summary>Stops the given partitions' workers: the running item is cancelled and queued items are dropped (Kafka redelivers them, as their offsets were never stored).</summary>
    public async Task StopAsync(IEnumerable<TopicPartition> partitions)
    {
        var stopping = new List<Task>();

        foreach (var partition in partitions)
        {
            if (_workers.TryRemove(partition, out var worker))
            {
                stopping.Add(worker.StopAsync());
            }
        }

        await Task.WhenAll(stopping);
    }

    public Task StopAllAsync()
    {
        return StopAsync(_workers.Keys.ToList());
    }

    private void Fail(Exception exception)
    {
        _failure ??= exception;
    }

    private sealed class Worker
    {
        private readonly CancellationTokenSource _cancel = new();
        private readonly Task _loop;

        public Worker(PartitionWorkerSet owner)
        {
            Queue = Channel.CreateBounded<Func<CancellationToken, Task>>(new BoundedChannelOptions(QueueCapacity)
            {
                SingleReader = true,
                FullMode = BoundedChannelFullMode.Wait
            });

            _loop = Task.Run(() => RunAsync(owner));
        }

        public Channel<Func<CancellationToken, Task>> Queue { get; }

        public async Task StopAsync()
        {
            Queue.Writer.TryComplete();
            _cancel.Cancel();
            await _loop;
            _cancel.Dispose();
        }

        private async Task RunAsync(PartitionWorkerSet owner)
        {
            try
            {
                await foreach (var work in Queue.Reader.ReadAllAsync(_cancel.Token))
                {
                    await work(_cancel.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                owner.Fail(ex);
            }
        }
    }
}
