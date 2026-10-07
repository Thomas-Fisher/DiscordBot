using DiscordBot.Application.Interfaces;
using DiscordBot.Domain.Entities;
using DiscordBot.Domain.Events;
using DiscordBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DiscordBot.Infrastructure.EventBus;

public class Outbox : IOutbox
{
    private readonly AppDbContext _context;
    private readonly IEventSerializer _serializer;
    private readonly OutboxSignal _signal;
    private readonly TimeProvider _timeProvider;

    public Outbox(AppDbContext context, IEventSerializer serializer, OutboxSignal signal, TimeProvider timeProvider)
    {
        _context = context;
        _serializer = serializer;
        _signal = signal;
        _timeProvider = timeProvider;
    }

    public void Stage(IDomainEvent @event)
    {
        var envelope = _serializer.Serialize(@event);

        _context.Outbox.Add(new OutboxMessage
        {
            Id = envelope.EventId,
            Type = envelope.Type,
            PartitionKey = envelope.PartitionKey,
            Payload = envelope.Payload,
            OccurredAt = envelope.OccurredAt,
            CreatedAt = _timeProvider.GetUtcNow().UtcDateTime
        });
    }

    public void DiscardStaged()
    {
        // Left tracked as "added", they would be stored by the next save in this scope, announcing something that never happened
        foreach (var staged in _context.ChangeTracker.Entries<OutboxMessage>().Where(e => e.State == EntityState.Added).ToList())
        {
            staged.State = EntityState.Detached;
        }
    }

    public async Task AddAsync(IDomainEvent @event, CancellationToken cancellationToken = default)
    {
        Stage(@event);
        await _context.SaveChangesAsync(cancellationToken);

        _signal.Notify();
    }
}
