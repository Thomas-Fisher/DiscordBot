using DiscordBot.Domain.Common;
using DiscordBot.Domain.Entities;
using DiscordBot.Domain.Enums;
using DiscordBot.Infrastructure.Persistence;
using DiscordBot.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace DiscordBot.Tests.Integration.Postgres;

/// <summary>
/// Behaviour that only PostgreSQL itself can prove: the signup advisory lock, keyset paging and bulk deletes on the
/// real schema, and that all migrations apply.
/// </summary>
public class PostgresRepositoryTests : IClassFixture<PostgresFixture>
{
    private readonly PostgresFixture _postgres;

    public PostgresRepositoryTests(PostgresFixture postgres)
    {
        _postgres = postgres;
    }

    private static Repositories RepositoriesOver(AppDbContext context) => new(context);

    private async Task<ulong> SeedEventAsync(ulong guildId = 1)
    {
        await using var context = _postgres.CreateContext();
        if (!await context.Guilds.AnyAsync(g => g.GuildId == guildId))
        {
            context.Guilds.Add(new Guild { GuildId = guildId, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        }

        var eventId = UniqueId.Next();
        context.Events.Add(new Event
        {
            EventId = eventId,
            GuildId = guildId,
            CreatedBy = 5,
            Title = "Raid",
            ScheduledTime = DateTime.UtcNow.AddDays(1),
            CreatedAt = DateTime.UtcNow,
            MessageId = eventId + 1,
            Type = EventType.WowClassic
        });
        await context.SaveChangesAsync();
        return eventId;
    }

    [Fact]
    public async Task Migrate_EveryMigration_IsAppliedAndTheExpectedIndexesExist()
    {
        await using var context = _postgres.CreateContext();

        var pending = await context.Database.GetPendingMigrationsAsync();
        var indexes = await context.Database
            .SqlQueryRaw<string>("select indexname as \"Value\" from pg_indexes where schemaname = 'public'")
            .ToListAsync();

        Assert.Empty(pending);
        Assert.Contains("IX_Events_MessageId", indexes);
        Assert.Contains("IX_Events_ScheduledTime_Active", indexes);
        Assert.Contains("IX_ActivityLogs_GuildId_Timestamp_LogId", indexes);
        Assert.Contains("IX_RefreshTokens_TokenHash", indexes);
        Assert.DoesNotContain("IX_MessageLogs_AuthorId_LoggedAt", indexes);
    }

    [Fact]
    public async Task SignUp_ThirtyPeopleAtTheSameTimeOnSeparateConnections_GetDistinctConsecutivePlaces()
    {
        var eventId = await SeedEventAsync();
        const int users = 30;

        // Every signup uses its own connection, the way separate reactions would
        var orders = await Task.WhenAll(Enumerable.Range(1, users).Select(async i =>
        {
            await using var context = _postgres.CreateContext();
            return await RepositoriesOver(context).Signups.SignUpAsync(eventId, (ulong)i, "mage", "fire");
        }));

        Assert.Equal(Enumerable.Range(1, users), orders.Select(o => o!.Value).OrderBy(x => x));
        await using var check = _postgres.CreateContext();
        var stored = await check.EventSignups.Where(s => s.EventId == eventId).Select(s => s.SignupOrder!.Value).ToListAsync();
        Assert.Equal(users, stored.Distinct().Count());
    }

    [Fact]
    public async Task SignUp_TheSameUserTenTimesAtOnce_ProducesOneRowWithoutErrors()
    {
        var eventId = await SeedEventAsync();

        await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
        {
            await using var context = _postgres.CreateContext();
            await RepositoriesOver(context).Signups.SignUpAsync(eventId, 777, "priest", "holy");
        }));

        await using var check = _postgres.CreateContext();
        var rows = await check.EventSignups.Where(s => s.EventId == eventId && s.UserId == 777).ToListAsync();
        Assert.Equal(1, Assert.Single(rows).SignupOrder);
    }

    [Fact]
    public async Task SignOff_HalfTheSignupsLeavingAtOnce_LeavesNoGapsOrDuplicatesInTheOrderNumbers()
    {
        var eventId = await SeedEventAsync();
        for (ulong user = 1; user <= 10; user++)
        {
            await using var context = _postgres.CreateContext();
            await RepositoriesOver(context).Signups.SignUpAsync(eventId, user, "mage", "fire");
        }

        // Some sign off and some become unsure, all at the same moment
        await Task.WhenAll(new ulong[] { 2, 4, 6, 8, 10 }.Select(async user =>
        {
            await using var context = _postgres.CreateContext();
            var signups = RepositoriesOver(context).Signups;
            if (user % 4 == 0)
            {
                await signups.MarkUnsureAsync(eventId, user, compactOrder: true);
            }
            else
            {
                await signups.SignOffAsync(eventId, user, compactOrder: true);
            }
        }));

        await using var check = _postgres.CreateContext();
        var remaining = await check.EventSignups
            .Where(s => s.EventId == eventId && s.Status == SignupStatus.SignedUp)
            .OrderBy(s => s.SignupOrder)
            .ToListAsync();
        Assert.Equal(new ulong[] { 1, 3, 5, 7, 9 }, remaining.Select(s => s.UserId));      // original relative order kept
        Assert.Equal(new int?[] { 1, 2, 3, 4, 5 }, remaining.Select(s => s.SignupOrder));   // consecutive, no duplicates
    }

    [Fact]
    public async Task Insert_FiveHundredSignupsGeneratedInOneMillisecond_NeverCollideOnThePrimaryKey()
    {
        var eventId = await SeedEventAsync();
        await using var context = _postgres.CreateContext();
        context.EventSignups.AddRange(Enumerable.Range(1, 500).Select(i => new EventSignup
        {
            SignupId = UniqueId.Next(),
            EventId = eventId,
            UserId = (ulong)(10_000 + i),
            Status = SignupStatus.SignedUp,
            SignedUpAt = DateTime.UtcNow
        }));

        // Would throw on a duplicate SignupId
        await context.SaveChangesAsync();

        Assert.Equal(500, await context.EventSignups.CountAsync(s => s.EventId == eventId && s.UserId > 10_000));
    }

    [Fact]
    public async Task DeleteOld_MessageAndActivityLogs_RemovesOnlyRowsOlderThanTheCutoff()
    {
        const ulong guild = 5151;
        await SeedEventAsync(guild);
        await using (var context = _postgres.CreateContext())
        {
            context.MessageLogs.AddRange(
                new MessageLog { MessageId = UniqueId.Next(), GuildId = guild, ChannelId = 1, AuthorId = 1, Content = "old", LoggedAt = DateTime.UtcNow.AddDays(-40) },
                new MessageLog { MessageId = UniqueId.Next(), GuildId = guild, ChannelId = 1, AuthorId = 1, Content = "old but flagged deleted", LoggedAt = DateTime.UtcNow.AddDays(-40), IsDeleted = true },
                new MessageLog { MessageId = UniqueId.Next(), GuildId = guild, ChannelId = 1, AuthorId = 1, Content = "recent", LoggedAt = DateTime.UtcNow.AddDays(-2) });
            context.ActivityLogs.AddRange(
                new ActivityLog { LogId = UniqueId.Next(), GuildId = guild, Timestamp = DateTime.UtcNow.AddDays(-100), ActionType = "Old", EntityType = "Event" },
                new ActivityLog { LogId = UniqueId.Next(), GuildId = guild, Timestamp = DateTime.UtcNow.AddDays(-10), ActionType = "Recent", EntityType = "Event" });
            await context.SaveChangesAsync();
        }

        await using var run = _postgres.CreateContext();
        var repositories = RepositoriesOver(run);

        await repositories.MessageLogs.DeleteOldMessagesAsync(30);
        await repositories.ActivityLogs.DeleteOldActivityLogsAsync(90);

        await using var check = _postgres.CreateContext();
        Assert.Equal(new[] { "recent" }, await check.MessageLogs.Where(m => m.GuildId == guild).Select(m => m.Content!).ToListAsync());
        Assert.Equal(new[] { "Recent" }, await check.ActivityLogs.Where(a => a.GuildId == guild).Select(a => a.ActionType).ToListAsync());
    }

    [Fact]
    public async Task LogMessages_SixConcurrentBatchesForAGuildNotSeenBefore_AllSucceedAndCreateTheGuildOnce()
    {
        const ulong newGuild = 6161;
        MessageLog Message() => new() { MessageId = UniqueId.Next(), GuildId = newGuild, ChannelId = 1, AuthorId = 2, Content = "hi", LoggedAt = DateTime.UtcNow };

        await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            await using var context = _postgres.CreateContext();
            await RepositoriesOver(context).MessageLogs.LogMessagesAsync(Enumerable.Range(0, 20).Select(_ => Message()).ToList());
        }));

        await using var check = _postgres.CreateContext();
        Assert.Equal(1, await check.Guilds.CountAsync(g => g.GuildId == newGuild));
        Assert.Equal(120, await check.MessageLogs.CountAsync(m => m.GuildId == newGuild));
    }

    [Fact]
    public async Task CompleteRecurringEventAndCreateNext_DueRecurringEvent_CompletesTheSourceAndCreatesTheNextTogether()
    {
        var eventId = await SeedEventAsync();
        await using var context = _postgres.CreateContext();
        var source = await context.Events.SingleAsync(e => e.EventId == eventId);
        source.IsRecurring = true;
        source.RecurrencePattern = RecurrencePattern.Weekly;
        source.ScheduledTime = DateTime.UtcNow.AddMinutes(-5);
        await context.SaveChangesAsync();

        var events = RepositoriesOver(context).Events;
        var due = (await events.GetDueRecurringEventsAsync()).Single(e => e.EventId == eventId);
        var next = new Event
        {
            EventId = UniqueId.Next(),
            GuildId = due.GuildId,
            CreatedBy = due.CreatedBy,
            Title = due.Title,
            ScheduledTime = due.ScheduledTime.AddDays(7),
            CreatedAt = DateTime.UtcNow,
            IsRecurring = true,
            RecurrencePattern = due.RecurrencePattern,
            ParentRecurringEventId = due.EventId
        };

        await events.CompleteRecurringEventAndCreateNextAsync(due, next);

        await using var check = _postgres.CreateContext();
        Assert.Equal(EventStatus.Completed, (await check.Events.SingleAsync(e => e.EventId == eventId)).Status);
        Assert.True(await check.Events.AnyAsync(e => e.EventId == next.EventId && e.Status == EventStatus.Scheduled));
        Assert.DoesNotContain(await RepositoriesOver(check).Events.GetDueRecurringEventsAsync(), e => e.EventId == eventId);
    }
}
