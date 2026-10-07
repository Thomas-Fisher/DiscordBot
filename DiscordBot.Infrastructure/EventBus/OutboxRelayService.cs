using DiscordBot.Application.Interfaces;
using DiscordBot.Domain.Entities;
using DiscordBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Infrastructure.EventBus;

/// <summary>Publishes the outbox oldest first. Marked published only after the bus accepts it, so a crash republishes. If the bus refuses one, the batch stops so nothing overtakes it. Run one relay per database.</summary>
public class OutboxRelayService : BackgroundService
{
    internal const int BatchSize = 100;

    // Locked for the run, and rows another relay has locked are skipped rather than waited for
    private const string ClaimSql = "SELECT * FROM \"Outbox\" WHERE \"PublishedAt\" IS NULL ORDER BY \"CreatedAt\", \"Id\" LIMIT 100 FOR UPDATE SKIP LOCKED";

    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan KeepPublishedFor = TimeSpan.FromDays(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OutboxSignal _signal;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OutboxRelayService> _logger;
    private DateTime _lastCleanupAt = DateTime.MinValue;

    public OutboxRelayService(IServiceScopeFactory scopeFactory, OutboxSignal signal, TimeProvider timeProvider, ILogger<OutboxRelayService> logger)
    {
        _scopeFactory = scopeFactory;
        _signal = signal;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = PollInterval;

            try
            {
                var (published, complete) = await RelayOnceAsync(stoppingToken);
                if (!complete)
                {
                    delay = RetryDelay;
                }
                else if (published == BatchSize)
                {
                    continue;   // a full batch: there is probably more
                }

                await CleanUpAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "The outbox relay failed; trying again in {Seconds} seconds", RetryDelay.TotalSeconds);
                delay = RetryDelay;
            }

            await _signal.WaitAsync(delay, stoppingToken);
        }
    }

    /// <summary>Publishes one batch. Complete is false when the bus refused a message, which ends the batch early.</summary>
    public async Task<(int Published, bool Complete)> RelayOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var bus = scope.ServiceProvider.GetRequiredService<IEventBus>();
        var serializer = scope.ServiceProvider.GetRequiredService<IEventSerializer>();

        // The database connection retries on temporary failures, and a transaction of our own has to run inside that retry. A retry
        // starts again from the top, so it may publish a message a second time, which consumers already cope with
        var strategy = db.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

            var waiting = await db.Outbox.FromSqlRaw(ClaimSql).ToListAsync(cancellationToken);

            var published = 0;
            var complete = true;

            foreach (var message in waiting)
            {
                try
                {
                    await bus.PublishAsync(serializer.Deserialize(ToEnvelope(message)));
                }
                catch (InvalidOperationException ex)
                {
                    // Unreadable for good (an unknown type, a broken payload): retrying cannot help and would block everything behind it
                    _logger.LogError(ex, "Dropping outbox message {MessageId} ({Type}): it cannot be read", message.Id, message.Type);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "The event bus did not accept outbox message {MessageId} ({Type}); it and everything after it stay waiting", message.Id, message.Type);
                    complete = false;
                    break;
                }

                message.PublishedAt = _timeProvider.GetUtcNow().UtcDateTime;
                published++;
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return (published, complete);
        });
    }

    private async Task CleanUpAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (now - _lastCleanupAt < TimeSpan.FromHours(1))
        {
            return;
        }

        _lastCleanupAt = now;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await BatchedDelete.OlderThanAsync(db, "Outbox", "PublishedAt", now - KeepPublishedFor);
    }

    private static EventEnvelope ToEnvelope(OutboxMessage message)
    {
        return new EventEnvelope(message.Type, message.Id, message.OccurredAt, message.PartitionKey, message.Payload);
    }
}
