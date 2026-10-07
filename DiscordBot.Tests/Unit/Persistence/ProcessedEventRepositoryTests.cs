using DiscordBot.Domain.Entities;
using DiscordBot.Infrastructure.Persistence.Repositories;
using DiscordBot.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace DiscordBot.Tests.Unit.Persistence;

public class ProcessedEventRepositoryTests
{
    private readonly TestDb _db = new();
    private readonly ProcessedEventRepository _repository;

    public ProcessedEventRepositoryTests()
    {
        _repository = new ProcessedEventRepository(_db.Context);
    }

    [Fact]
    public async Task HasProcessed_AfterMarking_IsTrueOnlyForThatEventAndHandler()
    {
        var eventId = Guid.NewGuid();
        await _repository.MarkProcessedAsync(eventId, "HandlerA");

        var same = await _repository.HasProcessedAsync(eventId, "HandlerA");
        var otherHandler = await _repository.HasProcessedAsync(eventId, "HandlerB");
        var otherEvent = await _repository.HasProcessedAsync(Guid.NewGuid(), "HandlerA");

        Assert.Equal((true, false, false), (same, otherHandler, otherEvent));
    }

    [Fact]
    public async Task MarkProcessed_MarkedTwice_KeepsOneRecord()
    {
        var eventId = Guid.NewGuid();

        await _repository.MarkProcessedAsync(eventId, "HandlerA");
        await _repository.MarkProcessedAsync(eventId, "HandlerA");

        using var context = _db.Open();
        Assert.Single(context.ProcessedEvents);
    }

    [Fact]
    public async Task DeleteOlderThan_OldAndRecentRecords_RemovesOnlyTheOldOnes()
    {
        _db.Context.ProcessedEvents.AddRange(
            new ProcessedEvent { EventId = Guid.NewGuid(), Handler = "H", ProcessedAt = DateTime.UtcNow.AddDays(-10) },
            new ProcessedEvent { EventId = Guid.NewGuid(), Handler = "H", ProcessedAt = DateTime.UtcNow.AddDays(-1) });
        await _db.Context.SaveChangesAsync();

        await _repository.DeleteOlderThanAsync(7);

        using var context = _db.Open();
        Assert.Single(await context.ProcessedEvents.ToListAsync());
    }
}
