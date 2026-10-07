using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DiscordBot.Discord;

public class DiscordHealthCheck : IHealthCheck
{
    private readonly DiscordSocketClient _client;

    public DiscordHealthCheck(DiscordSocketClient client)
    {
        _client = client;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var state = _client.ConnectionState;
        var data = new Dictionary<string, object>
        {
            ["connectionState"] = state.ToString(),
            ["latencyMs"] = _client.Latency,
            ["guilds"] = _client.Guilds.Count
        };

        var result = state switch
        {
            ConnectionState.Connected => HealthCheckResult.Healthy($"Connected to Discord in {_client.Guilds.Count} server(s)", data),
            ConnectionState.Connecting => HealthCheckResult.Degraded("Connecting to Discord", data: data),
            _ => HealthCheckResult.Unhealthy($"Not connected to Discord ({state})", data: data)
        };

        return Task.FromResult(result);
    }
}
