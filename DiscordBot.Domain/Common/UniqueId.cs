namespace DiscordBot.Domain.Common;

/// <summary>
/// Generates unique, strictly increasing, time-ordered ids for entities that use application-assigned keys.
/// Two calls in the same millisecond never return the same value.
/// </summary>
public static class UniqueId
{
    private static long _last;

    public static ulong Next()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        while (true)
        {
            var last = Interlocked.Read(ref _last);
            var candidate = Math.Max(now, last + 1);

            if (Interlocked.CompareExchange(ref _last, candidate, last) == last)
            {
                return (ulong)candidate;
            }
        }
    }
}
