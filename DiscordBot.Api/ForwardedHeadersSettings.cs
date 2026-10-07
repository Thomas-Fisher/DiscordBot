using DiscordBot.Application.Options;

namespace DiscordBot.Api;

/// <summary>Behind a reverse proxy the real client address and scheme arrive in X-Forwarded-* headers. They are only believed from the proxies listed here (off by default).</summary>
public sealed class ForwardedHeadersSettings : IValidatableOptions
{
    public const string SectionName = "ForwardedHeaders";

    public bool Enabled { get; set; }

    public List<string> KnownProxies { get; set; } = [];

    public IReadOnlyList<string> Validate()
    {
        var invalid = KnownProxies.Where(proxy => !System.Net.IPAddress.TryParse(proxy, out _)).ToList();

        return invalid.Count == 0
            ? []
            : [$"ForwardedHeaders:KnownProxies must be IP addresses: {string.Join(", ", invalid)}"];
    }
}
