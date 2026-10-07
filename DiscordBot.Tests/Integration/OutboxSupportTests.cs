using DiscordBot.Domain.Entities;
using DiscordBot.Infrastructure.EventBus;
using DiscordBot.Tests.Support;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DiscordBot.Tests.Integration;

public class OutboxSupportTests
{
    private readonly TestDb _db = new();

    [Fact]
    public async Task OutboxHealthCheck_NothingWaiting_IsHealthy()
    {
        var check = new OutboxHealthCheck(_db.Open(), TimeProvider.System);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task OutboxHealthCheck_MessageWaitingForTooLong_IsDegraded()
    {
        using (var context = _db.Open())
        {
            context.Outbox.Add(new OutboxMessage { Id = Guid.NewGuid(), Type = "reaction-added.v1", PartitionKey = "1", Payload = "{}", OccurredAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow.AddMinutes(-10) });
            await context.SaveChangesAsync();
        }

        var check = new OutboxHealthCheck(_db.Open(), TimeProvider.System);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Degraded, result.Status);
    }
}
