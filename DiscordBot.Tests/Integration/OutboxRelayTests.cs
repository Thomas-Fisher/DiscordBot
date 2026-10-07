using DiscordBot.Application.Interfaces;
using DiscordBot.Domain.Events;
using DiscordBot.Infrastructure.EventBus;
using DiscordBot.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiscordBot.Tests.Integration;

public class OutboxRelayTests
{
    private readonly TestDb _db = new();
    private readonly SwitchableEventBus _bus = new();
    private readonly OutboxRelayService _relay;

    public OutboxRelayTests()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _db.Open());
        services.AddSingleton<IEventBus>(_bus);
        services.AddSingleton<IEventSerializer, JsonEventSerializer>();

        _relay = new OutboxRelayService(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), new OutboxSignal(), TimeProvider.System, NullLogger<OutboxRelayService>.Instance);
    }

    private static ReactionAddedEvent Reaction(ulong messageId)
    {
        return new ReactionAddedEvent { MessageId = messageId, ChannelId = 2, UserId = 3, Emoji = "\u2705" };
    }

    private async Task<List<(ulong MessageId, bool Published)>> StoredAsync()
    {
        using var context = _db.Open();
        var rows = await context.Outbox.OrderBy(o => o.CreatedAt).ToListAsync();

        return rows.Select(r => (((ReactionAddedEvent)new JsonEventSerializer().Deserialize(new EventEnvelope(r.Type, r.Id, r.OccurredAt, r.PartitionKey, r.Payload))).MessageId, r.PublishedAt != null)).ToList();
    }

    [Fact]
    public async Task RelayOnce_WaitingMessages_ArePublishedInTheOrderTheyWereWrittenAndMarkedPublished()
    {
        await _db.Outbox.AddAsync(Reaction(1));
        await _db.Outbox.AddAsync(Reaction(2));

        var (published, complete) = await _relay.RelayOnceAsync(CancellationToken.None);

        Assert.Equal((2, true), (published, complete));
        Assert.Equal(new ulong[] { 1, 2 }, _bus.Published.Cast<ReactionAddedEvent>().Select(e => e.MessageId));
        Assert.All(await StoredAsync(), row => Assert.True(row.Published));
    }

    [Fact]
    public async Task RelayOnce_BusDown_KeepsEverythingWaitingInOrderAndPublishesItLater()
    {
        await _db.Outbox.AddAsync(Reaction(1));
        await _db.Outbox.AddAsync(Reaction(2));
        _bus.Failing = true;

        var whileDown = await _relay.RelayOnceAsync(CancellationToken.None);
        _bus.Failing = false;
        var afterwards = await _relay.RelayOnceAsync(CancellationToken.None);

        Assert.Equal((0, false), whileDown);
        Assert.Equal((2, true), afterwards);
        Assert.Equal(new ulong[] { 1, 2 }, _bus.Published.Cast<ReactionAddedEvent>().Select(e => e.MessageId));
    }

    [Fact]
    public async Task RelayOnce_MessageThatCanNeverBeRead_IsSkippedSoItCannotBlockTheOnesBehindIt()
    {
        using (var context = _db.Open())
        {
            context.Outbox.Add(new DiscordBot.Domain.Entities.OutboxMessage { Id = Guid.NewGuid(), Type = "no-such-event.v9", PartitionKey = "1", Payload = "{}", OccurredAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow.AddSeconds(-5) });
            await context.SaveChangesAsync();
        }

        await _db.Outbox.AddAsync(Reaction(7));

        var (published, complete) = await _relay.RelayOnceAsync(CancellationToken.None);

        Assert.Equal((2, true), (published, complete));
        Assert.Equal(7UL, Assert.Single(_bus.Published.Cast<ReactionAddedEvent>()).MessageId);
    }

    [Fact]
    public async Task RelayOnce_AlreadyPublishedMessages_AreNotPublishedAgain()
    {
        await _db.Outbox.AddAsync(Reaction(1));
        await _relay.RelayOnceAsync(CancellationToken.None);

        var (published, _) = await _relay.RelayOnceAsync(CancellationToken.None);

        Assert.Equal(0, published);
        Assert.Single(_bus.Published);
    }

    private sealed class SwitchableEventBus : IEventBus
    {
        public List<IDomainEvent> Published { get; } = new();

        public bool Failing { get; set; }

        public Task PublishAsync(IDomainEvent @event)
        {
            if (Failing)
            {
                throw new TimeoutException("broker down");
            }

            Published.Add(@event);

            return Task.CompletedTask;
        }
    }
}
