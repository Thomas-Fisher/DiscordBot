using System.Diagnostics;
using DiscordBot.Application.Telemetry;
using DiscordBot.Infrastructure.Background;
using DiscordBot.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiscordBot.Tests.Unit.Infrastructure;

public class BackgroundTaskQueueTests
{
    [Fact]
    public async Task Enqueue_SeveralItems_RunsThemInTheOrderTheyWereQueued()
    {
        var queue = new BackgroundTaskQueue("test", NullLogger<BackgroundTaskQueue>.Instance);
        await queue.StartAsync(CancellationToken.None);
        var results = new List<int>();
        var lastRan = new TaskCompletionSource();

        for (var i = 0; i < 20; i++)
        {
            var n = i;
            queue.Enqueue("test", _ =>
            {
                results.Add(n);
                if (n == 19)
                {
                    lastRan.SetResult();
                }

                return Task.CompletedTask;
            });
        }

        await lastRan.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await queue.StopAsync(CancellationToken.None);

        Assert.Equal(Enumerable.Range(0, 20), results);
    }

    [Fact]
    public async Task Enqueue_ItemThatThrows_DoesNotStopLaterItems()
    {
        var queue = new BackgroundTaskQueue("test", NullLogger<BackgroundTaskQueue>.Instance);
        await queue.StartAsync(CancellationToken.None);
        var laterItemRan = new TaskCompletionSource();

        queue.Enqueue("fails", _ => throw new InvalidOperationException("boom"));
        queue.Enqueue("after", _ =>
        {
            laterItemRan.SetResult();
            return Task.CompletedTask;
        });

        // Completes only if the later item ran
        await laterItemRan.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await queue.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task StopAsync_ItemsStillQueued_AreRunBeforeTheQueueStops()
    {
        // The worker starts asynchronously, so hold it on a gate item to be sure it is running while it is stopped
        var queue = new BackgroundTaskQueue("test", NullLogger<BackgroundTaskQueue>.Instance);
        await queue.StartAsync(CancellationToken.None);
        var workerRunning = new TaskCompletionSource();
        var gate = new TaskCompletionSource();
        queue.Enqueue("gate", async _ =>
        {
            workerRunning.SetResult();
            await gate.Task;
        });

        var ran = 0;
        for (var i = 0; i < 5; i++)
        {
            queue.Enqueue("pending", _ =>
            {
                Interlocked.Increment(ref ran);
                return Task.CompletedTask;
            });
        }

        await workerRunning.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var stopping = queue.StopAsync(CancellationToken.None);
        gate.SetResult();
        await stopping.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(5, ran);
    }

    [Fact]
    public async Task Enqueue_SlowItemInOneQueue_DoesNotHoldUpAnotherQueue()
    {
        var slow = new BackgroundTaskQueue("work", NullLogger<BackgroundTaskQueue>.Instance);
        var fast = new BackgroundTaskQueue("publish", NullLogger<BackgroundTaskQueue>.Instance);
        await slow.StartAsync(CancellationToken.None);
        await fast.StartAsync(CancellationToken.None);
        var release = new TaskCompletionSource();
        var fastRan = new TaskCompletionSource();

        slow.Enqueue("blocked", _ => release.Task);
        fast.Enqueue("quick", _ =>
        {
            fastRan.SetResult();
            return Task.CompletedTask;
        });

        await fastRan.Task.WaitAsync(TimeSpan.FromSeconds(5));
        release.SetResult();
        await slow.StopAsync(CancellationToken.None);
        await fast.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Depth_ItemsWaitingBehindABlockedOne_AreCounted()
    {
        var queue = new BackgroundTaskQueue("work", NullLogger<BackgroundTaskQueue>.Instance);
        await queue.StartAsync(CancellationToken.None);
        var release = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        queue.Enqueue("blocked", _ =>
        {
            started.SetResult();
            return release.Task;
        });
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        queue.Enqueue("waiting 1", _ => Task.CompletedTask);
        queue.Enqueue("waiting 2", _ => Task.CompletedTask);

        Assert.Equal(2, queue.Depth);
        release.SetResult();
        await queue.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Enqueue_InsideATrace_RunsTheWorkAsAChildOfThatTrace()
    {
        using var spans = new SpanRecorder();
        var queue = new BackgroundTaskQueue("test", NullLogger<BackgroundTaskQueue>.Instance);
        await queue.StartAsync(CancellationToken.None);
        var ran = new TaskCompletionSource();
        using var parent = BotTelemetry.Source.StartActivity("gateway event")!;

        queue.Enqueue("send", _ =>
        {
            ran.SetResult();
            return Task.CompletedTask;
        });
        await ran.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await queue.StopAsync(CancellationToken.None);

        var work = Assert.Single(spans.Spans, s => s.OperationName == "send");
        Assert.Equal(parent.TraceId, work.TraceId);
        Assert.Equal(parent.SpanId, work.ParentSpanId);
        Assert.Equal("test", work.GetTagItem("discordbot.queue"));
    }

    [Fact]
    public async Task Enqueue_ItemThatThrows_MarksItsSpanAsFailed()
    {
        using var spans = new SpanRecorder();
        var queue = new BackgroundTaskQueue("test", NullLogger<BackgroundTaskQueue>.Instance);
        await queue.StartAsync(CancellationToken.None);

        var laterItemRan = new TaskCompletionSource();
        queue.Enqueue("fails", _ => throw new InvalidOperationException("boom"));
        queue.Enqueue("after", _ =>
        {
            laterItemRan.SetResult();
            return Task.CompletedTask;
        });
        await laterItemRan.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await queue.StopAsync(CancellationToken.None);

        Assert.Equal(ActivityStatusCode.Error, Assert.Single(spans.Spans, s => s.OperationName == "fails").Status);
    }
}
