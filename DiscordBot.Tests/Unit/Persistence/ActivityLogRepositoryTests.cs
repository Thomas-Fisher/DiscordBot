using DiscordBot.Application.Models;
using DiscordBot.Domain.Entities;
using DiscordBot.Tests.Support;

namespace DiscordBot.Tests.Unit.Persistence;

public class ActivityLogRepositoryTests
{
    private const ulong GuildId = 10;

    private readonly TestDb _db = new();

    private static ActivityLog Log(ulong id, DateTime timestamp, string action = "Created", ulong guild = GuildId) => new()
    {
        LogId = id,
        GuildId = guild,
        Timestamp = timestamp,
        ActionType = action,
        EntityType = "Event"
    };

    private async Task StoreAsync(params ActivityLog[] logs)
    {
        _db.Context.ActivityLogs.AddRange(logs);
        await _db.Context.SaveChangesAsync();
    }

    private static IEnumerable<ActivityLog> OneLogPerMinute(int count)
    {
        var now = DateTime.UtcNow;
        return Enumerable.Range(1, count).Select(i => Log((ulong)i, now.AddMinutes(-i)));
    }

    [Fact]
    public async Task GetActivityLogsPage_ExactlyOnePageOfRows_HasNoCursor()
    {
        await StoreAsync(OneLogPerMinute(10).ToArray());

        var page = await _db.ActivityLogs.GetActivityLogsPageAsync(GuildId, null, 7, null, pageSize: 10);

        Assert.Equal(10, page.Items.Count);
        Assert.Null(page.Next);
    }

    [Fact]
    public async Task GetActivityLogsPage_FollowingTheCursorToTheEnd_VisitsEveryRowExactlyOnce()
    {
        await StoreAsync(OneLogPerMinute(25).ToArray());
        var seen = new List<ulong>();
        ActivityLogCursor? cursor = null;

        do
        {
            var page = await _db.ActivityLogs.GetActivityLogsPageAsync(GuildId, null, 7, cursor, pageSize: 10);
            seen.AddRange(page.Items.Select(l => l.LogId));
            cursor = page.Next;
        }
        while (cursor != null);

        Assert.Equal(Enumerable.Range(1, 25).Select(i => (ulong)i), seen);
    }

    [Fact]
    public async Task GetActivityLogsPage_NewLogsArrivingBetweenPages_DoNotShiftOrRepeatRows()
    {
        await StoreAsync(OneLogPerMinute(6).ToArray());
        var first = await _db.ActivityLogs.GetActivityLogsPageAsync(GuildId, null, 7, null, 3);
        await StoreAsync(Log(100, DateTime.UtcNow));

        var second = await _db.ActivityLogs.GetActivityLogsPageAsync(GuildId, null, 7, first.Next, 3);

        Assert.Equal(new ulong[] { 4, 5, 6 }, second.Items.Select(l => l.LogId));
    }

    [Fact]
    public async Task GetActivityLogsPage_RowsSharingATimestamp_AreOrderedByIdSoNoRowIsSkippedOrRepeated()
    {
        var timestamp = DateTime.UtcNow.AddMinutes(-1);
        await StoreAsync(Enumerable.Range(1, 6).Select(i => Log((ulong)i, timestamp)).ToArray());

        var first = await _db.ActivityLogs.GetActivityLogsPageAsync(GuildId, null, 7, null, 3);
        var second = await _db.ActivityLogs.GetActivityLogsPageAsync(GuildId, null, 7, first.Next, 3);

        Assert.Equal(new ulong[] { 6, 5, 4 }, first.Items.Select(l => l.LogId));
        Assert.Equal(new ulong[] { 3, 2, 1 }, second.Items.Select(l => l.LogId));
        Assert.Null(second.Next);
    }

    [Fact]
    public async Task GetActivityLogsPage_WithGuildActionAndAgeLimits_ReturnsOnlyMatchingRows()
    {
        var now = DateTime.UtcNow;
        await StoreAsync(
            Log(1, now.AddHours(-1), "Created"),
            Log(2, now.AddHours(-2), "Cancelled"),
            Log(3, now.AddDays(-10), "Created"),
            Log(4, now.AddHours(-1), "Created", guild: 99));

        var everything = await _db.ActivityLogs.GetActivityLogsPageAsync(GuildId, null, 7, null, 50);
        var onlyCreated = await _db.ActivityLogs.GetActivityLogsPageAsync(GuildId, "Created", 7, null, 50);

        Assert.Equal(new ulong[] { 1, 2 }, everything.Items.Select(l => l.LogId));
        Assert.Equal(new ulong[] { 1 }, onlyCreated.Items.Select(l => l.LogId));
    }
}
