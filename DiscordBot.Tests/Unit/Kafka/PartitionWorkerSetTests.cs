using Confluent.Kafka;
using DiscordBot.Infrastructure.EventBus;
using DiscordBot.Tests.Support;

namespace DiscordBot.Tests.Unit.Kafka;

public class PartitionWorkerSetTests
{
    private static TopicPartition Partition(int number) => new("topic", new Partition(number));

    [Fact]
    public async Task Submit_SeveralItemsForOnePartition_RunOneAtATimeInOrder()
    {
        var workers = new PartitionWorkerSet();
        var ran = new List<int>();
        var running = 0;
        var overlapped = false;
        var done = new TaskCompletionSource();

        for (var i = 0; i < 10; i++)
        {
            var n = i;
            await workers.SubmitAsync(Partition(0), async _ =>
            {
                if (Interlocked.Increment(ref running) > 1)
                {
                    overlapped = true;
                }

                await Task.Delay(5);
                ran.Add(n);
                Interlocked.Decrement(ref running);

                if (n == 9)
                {
                    done.SetResult();
                }
            }, CancellationToken.None);
        }

        await done.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await workers.StopAllAsync();

        Assert.Equal(Enumerable.Range(0, 10), ran);
        Assert.False(overlapped);
    }

    [Fact]
    public async Task Submit_OnePartitionIsStuck_OtherPartitionsKeepRunning()
    {
        var workers = new PartitionWorkerSet();
        var release = new TaskCompletionSource();
        var otherRan = new TaskCompletionSource();

        await workers.SubmitAsync(Partition(0), _ => release.Task, CancellationToken.None);
        await workers.SubmitAsync(Partition(1), _ =>
        {
            otherRan.SetResult();
            return Task.CompletedTask;
        }, CancellationToken.None);

        await otherRan.Task.WaitAsync(TimeSpan.FromSeconds(5));
        release.SetResult();
        await workers.StopAllAsync();
    }

    [Fact]
    public async Task Submit_WorkThatThrows_IsReportedAsAFailureOfTheSet()
    {
        var workers = new PartitionWorkerSet();

        await workers.SubmitAsync(Partition(0), _ => throw new InvalidOperationException("broken"), CancellationToken.None);
        await Eventually.UntilAsync(() => workers.Failure != null, "the worker set to report the failure");

        Assert.Equal("broken", workers.Failure!.Message);
        await workers.StopAllAsync();
    }

    [Fact]
    public async Task Stop_PartitionWithQueuedItems_CancelsTheRunningOneAndDropsTheRest()
    {
        var workers = new PartitionWorkerSet();
        var started = new TaskCompletionSource();
        var cancelled = new TaskCompletionSource();
        var laterRan = false;

        await workers.SubmitAsync(Partition(0), async cancellationToken =>
        {
            started.SetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                cancelled.SetResult();
                throw;
            }
        }, CancellationToken.None);
        await workers.SubmitAsync(Partition(0), _ =>
        {
            laterRan = true;
            return Task.CompletedTask;
        }, CancellationToken.None);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await workers.StopAsync(new[] { Partition(0) });

        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(laterRan);
    }

    [Fact]
    public async Task Submit_AfterThePartitionWasStopped_StartsAFreshWorker()
    {
        var workers = new PartitionWorkerSet();
        await workers.SubmitAsync(Partition(0), _ => Task.CompletedTask, CancellationToken.None);
        await workers.StopAsync(new[] { Partition(0) });
        var ran = new TaskCompletionSource();

        // The partition is assigned to this consumer again
        await workers.SubmitAsync(Partition(0), _ =>
        {
            ran.SetResult();
            return Task.CompletedTask;
        }, CancellationToken.None);

        await ran.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await workers.StopAllAsync();
    }
}
