using DiscordBot.Domain.Enums;
using System.Text.Json;
using DiscordBot.Domain.Events;
using DiscordBot.Application.Interfaces;
using DiscordBot.Application.Models;
using DiscordBot.Infrastructure.EventBus;
using DiscordBot.Infrastructure.Logging;
using DiscordBot.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DiscordBot.Tests.Unit.Infrastructure;

public class ActivityLoggerTests
{
    private readonly TestDb _db = new();
    private readonly ActivityLogger _logger;

    public ActivityLoggerTests()
    {
        _logger = new ActivityLogger(_db.ActivityLogs, _db.Outbox, NullLogger<ActivityLogger>.Instance);
    }

    [Fact]
    public async Task LogAsync_AllDetailsGiven_StoresThemInTheActivityLog()
    {
        var changes = new { Title = "Updated", Status = "Cancelled" };

        await _logger.LogAsync(new ActivityEntry(ActivityActionType.Updated, ActivityEntity.Event, 789, 123, 456, ActivityOrigin.Api, changes));

        using var context = _db.Open();
        var stored = await context.ActivityLogs.SingleAsync();
        Assert.Equal(("Updated", "Event", 789UL, 123UL, 456UL, "Api"),
            (stored.ActionType, stored.EntityType, stored.EntityId, stored.GuildId, stored.UserId, stored.Source));
        Assert.Equal("Updated", JsonDocument.Parse(stored.Changes!).RootElement.GetProperty("Title").GetString());
    }

    [Fact]
    public async Task LogAsync_EntryForAGuild_StoresAnActivityLogCreatedEventInTheOutboxWithTheRow()
    {
        await _logger.LogAsync(new ActivityEntry(ActivityActionType.Created, ActivityEntity.Event, 123, 456, 789, ActivityOrigin.Discord));

        using var context = _db.Open();
        var message = await context.Outbox.SingleAsync();
        var announced = Assert.IsType<ActivityLogCreatedEvent>(new JsonEventSerializer().Deserialize(new EventEnvelope(message.Type, message.Id, message.OccurredAt, message.PartitionKey, message.Payload)));
        Assert.Equal((456UL, "Created", 123UL), (announced.GuildId, announced.ActionType, announced.EntityId));
        Assert.Null(message.PublishedAt);
        Assert.Single(context.ActivityLogs);
    }

    [Fact]
    public async Task LogAsync_EntryWithoutAGuild_IsStoredButNothingIsPublished()
    {
        await _logger.LogAsync(new ActivityEntry(ActivityActionType.Created, ActivityEntity.Event, 123, null, 789, ActivityOrigin.Discord));

        using var context = _db.Open();
        Assert.Single(context.ActivityLogs);
        Assert.Empty(context.Outbox);
    }

    [Fact]
    public async Task LogAsync_StoringFails_DoesNotThrowAndPublishesNothing()
    {
        var failingStore = new Mock<IActivityLogRepository>();
        failingStore.Setup(s => s.CreateActivityLogAsync(It.IsAny<DiscordBot.Domain.Entities.ActivityLog>())).ThrowsAsync(new Exception("database down"));
        var logger = new ActivityLogger(failingStore.Object, _db.Outbox, NullLogger<ActivityLogger>.Instance);

        var exception = await Record.ExceptionAsync(() => logger.LogAsync(new ActivityEntry(ActivityActionType.Created, ActivityEntity.Event, 123, 456, 789, ActivityOrigin.Discord)));

        Assert.Null(exception);

        // The announcement is not left behind to be stored by a later save
        await _db.Context.SaveChangesAsync();
        using var context = _db.Open();
        Assert.Empty(context.Outbox);
    }
}
