using DiscordBot.Application;
using DiscordBot.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DiscordBot.Infrastructure.Background;

/// <summary>Reports how much work is waiting in each background queue. A queue that is nearly full is about to start dropping work.</summary>
public class BackgroundQueuesHealthCheck : IHealthCheck
{
    private const double WarningFraction = 0.8;

    private readonly IServiceProvider _services;

    public BackgroundQueuesHealthCheck(IServiceProvider services)
    {
        _services = services;
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>();
        var filling = new List<string>();

        foreach (var name in BackgroundQueues.All)
        {
            var queue = (BackgroundTaskQueue)_services.GetRequiredKeyedService<IBackgroundTaskQueue>(name);
            data[name] = queue.Depth;

            if (queue.Depth >= queue.Capacity * WarningFraction)
            {
                filling.Add(name);
            }
        }

        return Task.FromResult(filling.Count == 0
            ? HealthCheckResult.Healthy("Background queues have room", data)
            : HealthCheckResult.Degraded($"Background queue(s) nearly full: {string.Join(", ", filling)}", data: data));
    }
}
