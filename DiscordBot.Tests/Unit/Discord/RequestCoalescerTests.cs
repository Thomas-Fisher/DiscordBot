using System.Collections.Concurrent;
using DiscordBot.Discord.Services;
using DiscordBot.Tests.Support;
using Microsoft.Extensions.Time.Testing;

namespace DiscordBot.Tests.Unit.Discord;

public class RequestCoalescerTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(2);

    private readonly FakeTimeProvider _clock = new();

    private async Task ElapseWindowAsync(Func<bool> callbacksHaveRun)
    {
        _clock.Advance(Window);

        await Eventually.UntilAsync(callbacksHaveRun, "the callback to run after the window elapsed");
    }

    [Fact]
    public async Task Request_BurstForTheSameKey_FiresOnce()
    {
        var fired = new ConcurrentQueue<ulong>();
        var debouncer = new RequestCoalescer<ulong>(Window, fired.Enqueue, _clock);

        for (var i = 0; i < 50; i++)
        {
            debouncer.Request(7);
        }

        await ElapseWindowAsync(() => fired.Count >= 1);
        _clock.Advance(Window * 3);
        await Task.Delay(50);

        Assert.Equal(new ulong[] { 7 }, fired.ToArray());
    }

    [Fact]
    public async Task Request_DifferentKeys_EachFireOnce()
    {
        var fired = new ConcurrentBag<ulong>();
        var debouncer = new RequestCoalescer<ulong>(Window, fired.Add, _clock);

        debouncer.Request(1);
        debouncer.Request(2);
        debouncer.Request(1);
        debouncer.Request(3);
        await ElapseWindowAsync(() => fired.Count >= 3);

        Assert.Equal(new ulong[] { 1, 2, 3 }, fired.OrderBy(x => x));
    }

    [Fact]
    public async Task Request_AfterTheKeyAlreadyFired_FiresAgain()
    {
        var fired = new ConcurrentQueue<ulong>();
        var debouncer = new RequestCoalescer<ulong>(Window, fired.Enqueue, _clock);
        debouncer.Request(5);
        await ElapseWindowAsync(() => fired.Count == 1);

        // A change that arrives after the refresh started must not be lost
        debouncer.Request(5);
        await ElapseWindowAsync(() => fired.Count == 2);

        Assert.Equal(new ulong[] { 5, 5 }, fired.ToArray());
    }

    [Fact]
    public async Task Request_AfterTheCallbackThrew_TheKeyCanStillBeRequestedAgain()
    {
        var calls = 0;
        var debouncer = new RequestCoalescer<ulong>(Window, _ =>
        {
            Interlocked.Increment(ref calls);
            throw new InvalidOperationException("boom");
        }, _clock);
        debouncer.Request(9);
        await ElapseWindowAsync(() => Volatile.Read(ref calls) == 1);

        debouncer.Request(9);
        await ElapseWindowAsync(() => Volatile.Read(ref calls) == 2);

        Assert.Equal(2, Volatile.Read(ref calls));
    }
}
