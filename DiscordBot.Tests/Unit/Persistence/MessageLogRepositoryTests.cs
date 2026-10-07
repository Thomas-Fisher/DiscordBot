using DiscordBot.Domain.Entities;
using DiscordBot.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace DiscordBot.Tests.Unit.Persistence;

public class MessageLogRepositoryTests
{
    private const ulong GuildId = 10;

    private readonly TestDb _db = new();

    private static MessageLog Message(ulong id, ulong guild = GuildId) => new()
    {
        MessageId = id,
        GuildId = guild,
        ChannelId = 2,
        AuthorId = 3,
        Content = "hi",
        LoggedAt = DateTime.UtcNow
    };

    private async Task<List<ulong>> StoredMessageIdsAsync()
    {
        using var context = _db.Open();
        return (await context.MessageLogs.Select(m => m.MessageId).ToListAsync()).OrderBy(id => id).ToList();
    }

    [Fact]
    public async Task Find_StoredMessage_GivesItsAuthorAndText()
    {
        await _db.MessageLogs.LogMessagesAsync(new[] { Message(1) });

        var found = await _db.MessageLogs.FindAsync(1);

        Assert.Equal((3UL, "hi"), (found!.AuthorId, found.Content));
        Assert.Null(await _db.MessageLogs.FindAsync(2));
    }

    [Fact]
    public async Task LogMessages_MessagesFromNewGuilds_StoresThemAndCreatesEachMissingGuildOnce()
    {
        var batch = new[] { Message(1), Message(2), Message(3, guild: 77) };

        await _db.MessageLogs.LogMessagesAsync(batch);

        Assert.Equal(new ulong[] { 1, 2, 3 }, await StoredMessageIdsAsync());
        using var context = _db.Open();
        Assert.Equal(new ulong[] { GuildId, 77 }, (await context.Guilds.Select(g => g.GuildId).ToListAsync()).OrderBy(id => id));
    }

    [Fact]
    public async Task LogMessages_MessageFromAKnownGuild_DoesNotTouchTheGuild()
    {
        _db.Context.Guilds.Add(new Guild { GuildId = GuildId, GuildName = "Real name" });
        await _db.Context.SaveChangesAsync();

        await _db.MessageLogs.LogMessagesAsync(new[] { Message(1) });

        using var context = _db.Open();
        Assert.Equal("Real name", (await context.Guilds.SingleAsync()).GuildName);
    }

    [Fact]
    public async Task LogMessages_MessagesAlreadyStoredOrRepeatedInTheBatch_AreStoredOnlyOnce()
    {
        await _db.MessageLogs.LogMessagesAsync(new[] { Message(1) });

        await _db.MessageLogs.LogMessagesAsync(new[] { Message(1), Message(2), Message(2) });

        Assert.Equal(new ulong[] { 1, 2 }, await StoredMessageIdsAsync());
    }

    [Fact]
    public async Task MarkDeleted_StoredMessage_IsFlagged()
    {
        await _db.MessageLogs.LogMessagesAsync(new[] { Message(1), Message(2) });

        await _db.MessageLogs.MarkDeletedAsync(2);

        using var context = _db.Open();
        Assert.True((await context.MessageLogs.SingleAsync(m => m.MessageId == 2)).IsDeleted);
        Assert.False((await context.MessageLogs.SingleAsync(m => m.MessageId == 1)).IsDeleted);
    }
}
