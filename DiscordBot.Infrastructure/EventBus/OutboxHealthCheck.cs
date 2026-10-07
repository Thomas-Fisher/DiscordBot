using DiscordBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DiscordBot.Infrastructure.EventBus;

/// <summary>Degraded when events have been waiting to be published for too long: the relay (or the broker behind it) is stuck.</summary>
public class OutboxHealthCheck : IHealthCheck
{
    private static readonly TimeSpan TooOld = TimeSpan.FromMinutes(2);

    private readonly AppDbContext _context;
    private readonly TimeProvider _timeProvider;

    public OutboxHealthCheck(AppDbContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var oldest = await _context.Outbox
                .AsNoTracking()
                .Where(o => o.PublishedAt == null)
                .OrderBy(o => o.CreatedAt)
                .Select(o => (DateTime?)o.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);

            if (oldest is null)
            {
                return HealthCheckResult.Healthy("Nothing is waiting to be published");
            }

            var waiting = _timeProvider.GetUtcNow().UtcDateTime - oldest.Value;
            var data = new Dictionary<string, object> { ["oldestWaitingSeconds"] = (int)waiting.TotalSeconds };

            return waiting > TooOld
                ? HealthCheckResult.Degraded($"Events have been waiting {waiting.TotalMinutes:0} minutes to be published", data: data)
                : HealthCheckResult.Healthy("Events are being published", data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("The outbox check failed", ex);
        }
    }
}
