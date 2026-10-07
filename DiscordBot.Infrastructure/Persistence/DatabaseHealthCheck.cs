using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DiscordBot.Infrastructure.Persistence;

public class DatabaseHealthCheck : IHealthCheck
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    private readonly AppDbContext _context;

    public DatabaseHealthCheck(AppDbContext context)
    {
        _context = context;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);

        try
        {
            if (!await _context.Database.CanConnectAsync(timeout.Token))
            {
                return HealthCheckResult.Unhealthy("The database cannot be reached");
            }

            var data = new Dictionary<string, object>();

            var pending = (await _context.Database.GetPendingMigrationsAsync(timeout.Token)).ToList();
            data["pendingMigrations"] = pending.Count;

            if (pending.Count > 0)
            {
                return HealthCheckResult.Degraded($"The database is reachable but {pending.Count} migration(s) are not applied", data: data);
            }

            return HealthCheckResult.Healthy("The database is reachable and up to date", data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("The database check failed", ex);
        }
    }
}
