using DiscordBot.Application.Models;
using DiscordBot.Discord.Presentation;
using DiscordBot.Domain.Entities;

namespace DiscordBot.Tests.Unit.Discord;

public class EventRosterEmbedTests
{
    private static readonly Event Raid = new() { EventId = 5, GuildId = 1, Title = "SoO Raid", ScheduledTime = new DateTime(2026, 10, 8, 19, 0, 0, DateTimeKind.Utc) };

    private static string? Names(ulong id) => $"Player{id}";

    private static readonly List<PingRecipient> Ten =
    [
        new(1, 1, "warrior", "protection"), new(2, 1, "priest", "holy"), new(3, 1, "mage", "fire"), new(4, 1, "rogue", "assassination"), new(5, 1, "hunter", "marksman"),
        new(6, 2, "paladin", "protection"), new(7, 2, "druid", "restoration"), new(8, 2, "monk", "windwalker"), new(9, 2, "warlock", "affliction"), new(10, 2, "mage", "frost")
    ];

    private static string Field(global::Discord.Embed embed, string namePrefix) => embed.Fields.Single(f => f.Name.StartsWith(namePrefix)).Value;

    [Fact]
    public void Build_Roster_TitlesItWithTheEventAndSaysWhenItStarts()
    {
        var embed = EventRosterEmbed.Build(Raid, Ten, Names);

        Assert.Equal("📋 SoO Raid roster", embed.Title);
        Assert.Contains("<t:", embed.Description);
    }

    [Fact]
    public void Build_Roster_SummarisesTanksHealersMeleeAndRanged()
    {
        var embed = EventRosterEmbed.Build(Raid, Ten, Names);

        var summary = Field(embed, "Roster (10)");
        Assert.Contains("Tanks **2**", summary);
        Assert.Contains("Healers **2**", summary);
        Assert.Contains("Melee **2**", summary);
        Assert.Contains("Ranged **4**", summary);
    }

    [Fact]
    public void Build_Roster_ListsEachGroupWithSpecIconsAndNames()
    {
        var embed = EventRosterEmbed.Build(Raid, Ten, Names);

        var group1 = Field(embed, "Group 1 (5/5)");
        Assert.Contains("warrior_protection", group1);
        Assert.Contains("Player1", group1);
        Assert.Equal(5, group1.Split('\n').Length);
        Assert.Contains("Player6", Field(embed, "Group 2 (5/5)"));
    }

    [Fact]
    public void Build_Roster_NamesAMemberWhoLeftByMentionAndMarksSomeoneUnsure()
    {
        var roster = new List<PingRecipient> { new(1, 1, "mage", "fire", IsUnsure: true), new(2, 1) };

        var embed = EventRosterEmbed.Build(Raid, roster, id => id == 1 ? "Fireball Fred" : null);

        var group = Field(embed, "Group 1");
        Assert.Contains("Fireball Fred 🤷", group);
        Assert.Contains("<@2>", group);
    }

    [Fact]
    public void Build_Roster_CompletesTheLastRowOfGroups()
    {
        var embed = EventRosterEmbed.Build(Raid, Ten, Names);

        // Two groups fill two of the three places on a row; one empty field completes it
        Assert.Equal(1, embed.Fields.Count(f => f.Name == "\u200b"));
    }

    [Fact]
    public void Build_FullFortyManRoster_StaysWithinDiscordLimits()
    {
        var classes = new[] { ("warrior", "protection"), ("priest", "holy"), ("mage", "fire"), ("rogue", "assassination"), ("hunter", "marksman") };
        var forty = Enumerable.Range(1, 40).Select(i => new PingRecipient((ulong)i, (i - 1) / 5 + 1, classes[i % 5].Item1, classes[i % 5].Item2)).ToList();

        var embed = EventRosterEmbed.Build(Raid, forty, id => $"A rather long player name {id}");

        Assert.True(embed.Fields.Length <= 25, $"{embed.Fields.Length} fields");
        Assert.True(embed.Length <= 6000, $"{embed.Length} characters");
        Assert.Equal(8, embed.Fields.Count(f => f.Name.StartsWith("Group ")));
    }
}
