namespace DiscordBot.Api.Middleware;

public class RateLimitingOptions
{
    public bool Enabled { get; set; } = true;
    public int WindowMinutes { get; set; } = 1;
    public int MaxRequestsPerWindow { get; set; } = 100;
    public List<string> BypassIPs { get; set; } = new();

    /// <summary>Limits for groups of endpoints. The key is a path, or a path prefix ending in <c>*</c>; the first match wins.</summary>
    public Dictionary<string, int> EndpointLimits { get; set; } = new();
}
