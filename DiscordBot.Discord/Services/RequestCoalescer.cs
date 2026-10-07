using System.Collections.Concurrent;

namespace DiscordBot.Discord.Services;

/// <summary>
/// Collapses bursts of requests for a key into one callback. Requests during the wait are absorbed; one arriving after the
/// callback has started schedules another, so the latest state is never missed.
/// </summary>
public class RequestCoalescer<TKey> where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, byte> _pending = new();
    private readonly TimeSpan _window;
    private readonly Action<TKey> _onElapsed;
    private readonly TimeProvider _time;

    public RequestCoalescer(TimeSpan window, Action<TKey> onElapsed, TimeProvider? time = null)
    {
        _window = window;
        _onElapsed = onElapsed;
        _time = time ?? TimeProvider.System;
    }

    public void Request(TKey key)
    {
        if (!_pending.TryAdd(key, 0))
        {
            return;
        }

        _ = FireAfterWindowAsync(key);
    }

    private async Task FireAfterWindowAsync(TKey key)
    {
        try
        {
            await Task.Delay(_window, _time);
        }
        finally
        {
            // Clear before firing: requests arriving from here on are for state the callback might not see
            _pending.TryRemove(key, out _);
        }

        try
        {
            _onElapsed(key);
        }
        catch
        {
            // The callback only enqueues work; nothing here should be able to fault the timer task
        }
    }
}
