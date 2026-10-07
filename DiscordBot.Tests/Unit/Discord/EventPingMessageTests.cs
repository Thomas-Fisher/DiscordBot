using DiscordBot.Application.Models;
using DiscordBot.Discord.Presentation;
using DiscordBot.Domain.Entities;

namespace DiscordBot.Tests.Unit.Discord;

public class EventPingMessageTests
{
    private static readonly Event Raid = new() { EventId = 5, GuildId = 1, Title = "SoO Raid", ScheduledTime = new DateTime(2026, 10, 8, 19, 0, 0, DateTimeKind.Utc) };

    private static List<PingRecipient> People(int count, int? group = null) => Enumerable.Range(1, count).Select(i => new PingRecipient((ulong)i, group)).ToList();

    [Fact]
    public void Build_NoResponse_MentionsEveryoneAndSaysWhichEventAndWhenItStarts()
    {
        var chunk = Assert.Single(EventPingMessage.Build(Raid, PingTarget.NoResponse, "Please answer today", People(3)));

        Assert.StartsWith("Please answer today\n<@1> <@2> <@3> you haven't responded to **SoO Raid** yet (starts <t:", chunk.Text);
        Assert.Equal(new ulong[] { 1, 2, 3 }, chunk.UserIds);
    }

    [Fact]
    public void Build_Roster_IsTheNoteAndOneLineOfMentionsBecauseTheRosterItselfIsAnEmbed()
    {
        var recipients = new List<PingRecipient> { new(1, 1), new(2, 1), new(3, 2) };

        var chunk = Assert.Single(EventPingMessage.Build(Raid, PingTarget.Roster, "Raid opens 15 minutes before", recipients));

        Assert.Equal("Raid opens 15 minutes before\n📣 <@1> <@2> <@3>", chunk.Text);
        Assert.Equal(new ulong[] { 1, 2, 3 }, chunk.UserIds);
    }

    [Fact]
    public void Build_Standby_SaysTheyAreOnStandbyForTheEvent()
    {
        var chunk = Assert.Single(EventPingMessage.Build(Raid, PingTarget.Standby, null, People(2)));

        Assert.Equal("⏳ **Standby for SoO Raid:** <@1> <@2>", chunk.Text);
    }

    [Fact]
    public void Build_TitleFromAnOrganiser_CannotFormatTheMessage()
    {
        var evt = new Event { EventId = 5, GuildId = 1, Title = "**raid** ||x||", ScheduledTime = Raid.ScheduledTime };

        var chunk = Assert.Single(EventPingMessage.Build(evt, PingTarget.Standby, null, People(1)));

        Assert.Contains("\\*\\*raid\\*\\* \\|\\|x\\|\\|", chunk.Text);
    }

    [Fact]
    public void Build_VeryManyPeople_SplitsIntoMessagesThatFitDiscordsLimitsAndMentionEachPersonOnce()
    {
        var chunks = EventPingMessage.Build(Raid, PingTarget.NoResponse, "Note", People(450));

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c =>
        {
            Assert.True(c.Text.Length <= 2000, $"{c.Text.Length} characters");
            Assert.True(c.UserIds.Count <= 100);
        });
        Assert.Equal(Enumerable.Range(1, 450).Select(i => (ulong)i), chunks.SelectMany(c => c.UserIds));
        Assert.StartsWith("Note", chunks[0].Text);
        Assert.Contains("you haven't responded", chunks[^1].Text);
    }

    [Fact]
    public void Build_RosterOfFortyInEightGroups_FitsOneMessage()
    {
        var recipients = Enumerable.Range(1, 40).Select(i => new PingRecipient((ulong)(100000000000000000UL + (ulong)i), (i - 1) / 5 + 1)).ToList();

        var chunks = EventPingMessage.Build(Raid, PingTarget.Roster, "See you there", recipients);

        Assert.Single(chunks);
        Assert.Equal(40, chunks[0].UserIds.Count);
    }
}
