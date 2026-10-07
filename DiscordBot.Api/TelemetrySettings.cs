using DiscordBot.Application.Options;

namespace DiscordBot.Api;

/// <summary>Where traces, metrics and logs are sent. Leave the endpoint empty to send them nowhere.</summary>
public sealed class TelemetrySettings : IValidatableOptions
{
    public const string SectionName = "OpenTelemetry";

    /// <summary>Address of an OTLP/gRPC collector, e.g. http://localhost:4317.</summary>
    public string Endpoint { get; set; } = "";

    public bool Enabled => !string.IsNullOrWhiteSpace(Endpoint);

    public IReadOnlyList<string> Validate()
    {
        return !Enabled || Uri.TryCreate(Endpoint, UriKind.Absolute, out _)
            ? []
            : ["OpenTelemetry:Endpoint must be a full URL such as http://localhost:4317 (or empty to switch telemetry off)"];
    }
}
