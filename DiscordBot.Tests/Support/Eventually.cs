namespace DiscordBot.Tests.Support;

public static class Eventually
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    public static async Task UntilAsync(Func<bool> condition, string what, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException($"Timed out waiting for {what}");
            }

            await Task.Delay(10);
        }
    }

    public static async Task<T> ProducesAsync<T>(Func<T?> probe, string what, TimeSpan? timeout = null, TimeSpan? pause = null) where T : class
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        while (true)
        {
            var value = probe();
            if (value != null)
            {
                return value;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException($"Timed out waiting for {what}");
            }

            await Task.Delay(pause ?? TimeSpan.FromMilliseconds(10));
        }
    }
}
