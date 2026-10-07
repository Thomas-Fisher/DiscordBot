using DiscordBot.Domain.Entities;
using DiscordBot.Domain.Enums;
using DiscordBot.Discord.Presentation;

namespace DiscordBot.Tests.Unit.Discord;

public class EventEmbedBuilderTests
{
    private static Event Evt(EventType type = EventType.Generic, bool recurring = false) => new()
    {
        EventId = 1234,
        Title = "Raid night",
        Description = "Bring flasks",
        ScheduledTime = new DateTime(2026, 10, 1, 19, 30, 0, DateTimeKind.Utc),
        Type = type,
        IsRecurring = recurring,
        RecurrencePattern = recurring ? RecurrencePattern.Weekly : null
    };

    private static EventSignup Signup(ulong user, SignupStatus status = SignupStatus.SignedUp, string? cls = null, string? spec = null, int? order = null) => new()
    {
        SignupId = user,
        UserId = user,
        EventId = 1234,
        Status = status,
        WowClass = cls,
        WowSpec = spec,
        SignupOrder = order
    };

    private static string? NoNames(ulong _) => null;

    private static string Field(global::Discord.Embed embed, string namePrefix) =>
        embed.Fields.Single(f => f.Name.StartsWith(namePrefix)).Value;

    [Fact]
    public void Build_EventWithNoSignups_ShowsTheDetailsAndPlaceholders()
    {
        var embed = EventEmbedBuilder.Build(Evt(), Array.Empty<EventSignup>(), NoNames);

        Assert.Equal(("Raid night", "Bring flasks"), (embed.Title, embed.Description));
        Assert.Equal("None yet", Field(embed, "✅ Signed Up (0)"));
        Assert.Equal("None yet", Field(embed, "🤷 Unsure (0)"));
        Assert.Equal("None yet", Field(embed, "❌ Signed Off (0)"));
        Assert.Equal("`1234`", Field(embed, "🆔"));
    }

    [Fact]
    public void Build_GenericEventWithSignups_ListsEachStatusGroupWithMentionsAndCounts()
    {
        var signups = new[] { Signup(1), Signup(2), Signup(3, SignupStatus.Unsure), Signup(4, SignupStatus.SignedOff) };

        var embed = EventEmbedBuilder.Build(Evt(), signups, NoNames);

        Assert.Equal("<@1>, <@2>", Field(embed, "✅ Signed Up (2)"));
        Assert.Equal("<@3>", Field(embed, "🤷 Unsure (1)"));
        Assert.Equal("<@4>", Field(embed, "❌ Signed Off (1)"));
    }

    [Fact]
    public void Build_CancelledEvent_IsClearlyMarkedAndNoLongerAsksForReactions()
    {
        var evt = Evt();
        evt.Status = EventStatus.Cancelled;

        var embed = EventEmbedBuilder.Build(evt, Array.Empty<EventSignup>(), NoNames);

        Assert.StartsWith("❌ CANCELLED", embed.Title);
        Assert.Contains("Raid night", embed.Title);
        Assert.DoesNotContain("React", embed.Footer!.Value.Text);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Build_RecurringOrOneOffEvent_ShowsTheRecurrenceFieldOnlyForRecurringOnes(bool recurring, bool expectedField)
    {
        var evt = Evt(recurring: recurring);

        var embed = EventEmbedBuilder.Build(evt, Array.Empty<EventSignup>(), NoNames);

        Assert.Equal(expectedField, embed.Fields.Any(f => f.Name.Contains("Recurrence")));
    }

    [Fact]
    public void Build_GenericEvent_ShowsTheTimesAsDiscordTimestampsWhateverTheServersCultureOrZone()
    {
        var embed = EventEmbedBuilder.Build(Evt(), Array.Empty<EventSignup>(), NoNames);

        Assert.Equal("<t:1790883000:D>\n<t:1790883000:R>", Field(embed, "📅 Date"));
        Assert.Equal("<t:1790883000:t>", Field(embed, "🕐 Time"));
    }

    [Fact]
    public void Build_EventWithADuration_ShowsHowLongItLastsAndWhenItEnds()
    {
        var evt = Evt();
        evt.DurationMinutes = 150;

        var embed = EventEmbedBuilder.Build(evt, Array.Empty<EventSignup>(), NoNames);

        Assert.Equal("2h 30m (until <t:1790892000:t>)", Field(embed, "⏱️ Duration"));
    }

    [Fact]
    public void Build_EventWithoutADuration_ShowsNoDurationField()
    {
        var embed = EventEmbedBuilder.Build(Evt(), Array.Empty<EventSignup>(), NoNames);

        Assert.DoesNotContain(embed.Fields, f => f.Name.Contains("Duration"));
    }

    [Fact]
    public void Build_EventWithRolesToPing_ListsThemAsRoleMentions()
    {
        var evt = Evt();
        evt.MentionRoleIds = new List<ulong> { 111, 222 };

        var embed = EventEmbedBuilder.Build(evt, Array.Empty<EventSignup>(), NoNames);

        Assert.Equal("<@&111> <@&222>", Field(embed, "📣 Pings"));
    }

    private static string Column(global::Discord.Embed embed, string nameContains) =>
        embed.Fields.Single(f => f.Name.Contains(nameContains)).Value;

    private static readonly EventSignup[] Raid =
    {
        Signup(1, cls: "warrior", spec: "protection", order: 1),
        Signup(2, cls: "priest", spec: "holy", order: 2),
        Signup(3, cls: "warrior", spec: "arms", order: 3),
        Signup(4, cls: "mage", spec: "fire", order: 4),
        Signup(5, cls: "hunter", spec: "marksman", order: 5),
        Signup(6, SignupStatus.Unsure, cls: "paladin", spec: "holy"),
        Signup(7, SignupStatus.SignedOff)
    };

    private static string? RaidNames(ulong id) => $"Player{id}";

    [Fact]
    public void Build_WowEvent_ListsSignupsUnderTheirClassWithSpecIconsAndCounts()
    {
        var embed = EventEmbedBuilder.Build(Evt(EventType.WowClassic), Raid, RaidNames);

        var warriors = Column(embed, "Warrior (2)");
        Assert.Contains("Player1", warriors);
        Assert.Contains("warrior_protection", warriors);
        Assert.Contains("Player3", warriors);
        Assert.Contains("warrior_arms", warriors);
        Assert.Contains("priest_holy", Column(embed, "Priest (1)"));
        Assert.Contains("Player4", Column(embed, "Mage (1)"));
        Assert.Contains("Player6", Column(embed, "Unsure (1)"));
        Assert.Equal("Player7", Column(embed, "Signed Off (1)"));
    }

    [Fact]
    public void Build_WowEvent_SummarisesTanksHealersAndDpsWithTheMeleeRangedSplit()
    {
        var embed = EventEmbedBuilder.Build(Evt(EventType.WowClassic), Raid, RaidNames);

        var summary = Column(embed, "Signed Up (5)");
        Assert.Contains("Tanks **1**", summary);
        Assert.Contains("Healers **1**", summary);
        Assert.Contains("DPS **3** (Melee 1 · Ranged 2)", summary);
    }

    private static EventSignup InGroup(EventSignup signup, int group)
    {
        signup.GroupNumber = group;

        return signup;
    }

    [Fact]
    public void Build_WowEventWithARosterOnTheDashboard_LooksExactlyAsItDoesWithout()
    {
        var withGroups = new[]
        {
            InGroup(Signup(1, cls: "warrior", spec: "protection", order: 1), 1),
            InGroup(Signup(2, cls: "priest", spec: "holy", order: 2), 1),
            InGroup(Signup(3, SignupStatus.Unsure, cls: "mage", spec: "fire"), 2),
            Signup(4, cls: "rogue", spec: "assassination", order: 4)
        };
        var without = withGroups.Select(s => new EventSignup { SignupId = s.SignupId, UserId = s.UserId, EventId = s.EventId, Status = s.Status, WowClass = s.WowClass, WowSpec = s.WowSpec, SignupOrder = s.SignupOrder }).ToArray();

        var embedWith = EventEmbedBuilder.Build(Evt(EventType.WowClassic), withGroups, RaidNames, MessageUrl);
        var embedWithout = EventEmbedBuilder.Build(Evt(EventType.WowClassic), without, RaidNames, MessageUrl);

        Assert.Equal(embedWithout.Fields.Select(f => (f.Name, f.Value, f.Inline)), embedWith.Fields.Select(f => (f.Name, f.Value, f.Inline)));
        Assert.DoesNotContain(embedWith.Fields, f => f.Name.Contains("Roster") || f.Name.Contains("Group") || f.Name.Contains("Standby"));
    }

    private const string MessageUrl = "https://discord.com/channels/1/2/3";

    [Fact]
    public void Build_WowEventWithTheMessageLink_TurnsEachRoleCountIntoALinkWhoseHoverTextListsThePeople()
    {
        var embed = EventEmbedBuilder.Build(Evt(EventType.WowClassic), Raid, RaidNames, MessageUrl);

        var summary = Column(embed, "Signed Up (5)");
        Assert.Contains($"Tanks [**1**]({MessageUrl} \"Player1\")", summary);
        Assert.Contains($"Healers [**1**]({MessageUrl} \"Player2\")", summary);
        Assert.Contains($"DPS [**3**]({MessageUrl} \"Melee: Player3 · Ranged: Player4, Player5\") (Melee 1 · Ranged 2)", summary);
    }

    [Fact]
    public void Build_WowEventWithoutTheMessageLink_KeepsThePlainCounts()
    {
        var embed = EventEmbedBuilder.Build(Evt(EventType.WowClassic), Raid, RaidNames);

        Assert.DoesNotContain("http", Column(embed, "Signed Up (5)"));
    }

    [Fact]
    public void Build_WowEventWithTheMessageLink_LeavesAnEmptyRoleAsAPlainZero()
    {
        var embed = EventEmbedBuilder.Build(Evt(EventType.WowForever), Array.Empty<EventSignup>(), NoNames, MessageUrl);

        var summary = Column(embed, "Signed Up (0)");
        Assert.Contains("Tanks **0**", summary);
        Assert.DoesNotContain("http", summary);
    }

    [Fact]
    public void Build_WowEventWithTheMessageLink_NamesAMemberWhoLeftByIdAndKeepsQuotesFromEndingTheHoverText()
    {
        var signups = new[] { Signup(1, cls: "warrior", spec: "protection", order: 1), Signup(2, cls: "warrior", spec: "protection", order: 2) };

        var embed = EventEmbedBuilder.Build(Evt(EventType.WowClassic), signups, id => id == 2 ? "Say \"hi\"" : null, MessageUrl);

        Assert.Contains($"Tanks [**2**]({MessageUrl} \"User 1, Say 'hi'\")", Column(embed, "Signed Up (2)"));
    }

    [Fact]
    public void Build_WowEventWithTheMessageLink_CutsALongListOfNamesAndStaysWithinTheFieldLimit()
    {
        var tanks = Enumerable.Range(1, 40).Select(i => Signup((ulong)i, cls: "warrior", spec: "protection", order: i)).ToArray();

        var embed = EventEmbedBuilder.Build(Evt(EventType.WowClassic), tanks, id => $"A fairly long name {id}", MessageUrl);

        var summary = Column(embed, "Signed Up (40)");
        Assert.Contains("more\")", summary);
        Assert.True(summary.Length <= EventEmbedBuilder.MaxFieldValueLength, $"{summary.Length} characters");
    }

    [Fact]
    public void Build_WowEvent_ShowsTheSignedUpSummaryBeforeTheClasses()
    {
        var embed = EventEmbedBuilder.Build(Evt(EventType.WowClassic), Raid, RaidNames);

        var names = embed.Fields.Select(f => f.Name).ToList();
        Assert.True(names.FindIndex(n => n.Contains("Signed Up (5)")) < names.FindIndex(n => n.Contains("Warrior")), string.Join(" | ", names));
    }

    [Fact]
    public void Build_WowEvent_CompletesTheLastRowOfClassesSoUnsureStartsOnANewRow()
    {
        var embed = EventEmbedBuilder.Build(Evt(EventType.WowClassic), Raid, RaidNames);

        var fields = embed.Fields.ToList();
        var firstClass = fields.FindIndex(f => f.Name.Contains("Hunter"));
        var unsure = fields.FindIndex(f => f.Name.Contains("Unsure"));
        Assert.True(firstClass >= 0 && unsure > firstClass, "the class and unsure fields were not found");
        Assert.Equal(0, (unsure - firstClass) % 3);
        Assert.All(fields.Skip(firstClass).Take(unsure - firstClass), f => Assert.True(f.Inline));
    }

    [Fact]
    public void Build_WowEventWithNoSignups_ShowsZeroCountsAndNoClasses()
    {
        var embed = EventEmbedBuilder.Build(Evt(EventType.WowForever), Array.Empty<EventSignup>(), NoNames);

        Assert.Contains("Tanks **0**", Column(embed, "Signed Up (0)"));
        Assert.Equal("None yet", Column(embed, "Unsure (0)"));
        Assert.Equal("None yet", Column(embed, "Signed Off (0)"));
        Assert.DoesNotContain(embed.Fields, f => f.Name.Contains("Warrior"));
    }

    [Fact]
    public void Build_WowSignupWithoutASpec_IsListedSeparatelyByName()
    {
        var signups = new[] { Signup(9, order: 1) };

        var embed = EventEmbedBuilder.Build(Evt(EventType.WowClassic), signups, RaidNames);

        Assert.Equal("Player9", Column(embed, "No spec chosen (1)"));
    }

    [Fact]
    public void Build_WowEvent_ShowsTheTimesAsDiscordTimestampsSoEveryoneSeesTheirOwnTimezone()
    {
        var evt = Evt(EventType.WowClassic);
        evt.DurationMinutes = 150;

        var embed = EventEmbedBuilder.Build(evt, Array.Empty<EventSignup>(), NoNames);

        Assert.Equal("<t:1790883000:D>\n<t:1790883000:R>", Column(embed, "Date"));
        Assert.Equal("<t:1790883000:t>", Column(embed, "Time"));
        Assert.Equal("2h 30m (until <t:1790892000:t>)", Column(embed, "Duration"));
    }

    [Fact]
    public void Build_WowEvent_FooterCarriesTheTypeRecurrenceAndId()
    {
        var evt = Evt(EventType.WowClassic, recurring: true);

        var embed = EventEmbedBuilder.Build(evt, Raid, RaidNames);

        var footer = embed.Footer!.Value.Text;
        Assert.StartsWith("WowClassic · 🔄 Weekly · ID 1234", footer);
        Assert.Contains("React with your class", footer);
    }

    [Fact]
    public void Build_WowEvent_DoesNotRepeatThePingedRolesBecauseTheyAreTheMessageText()
    {
        var evt = Evt(EventType.WowClassic);
        evt.MentionRoleIds = new List<ulong> { 111 };

        var embed = EventEmbedBuilder.Build(evt, Array.Empty<EventSignup>(), NoNames);

        Assert.DoesNotContain(embed.Fields, f => f.Name.Contains("Pings"));
    }

    [Fact]
    public void Build_HundredsOfSignups_TruncatesTheListToDiscordsFieldLimit()
    {
        var signups = Enumerable.Range(1, 300).Select(i => Signup((ulong)(1_000_000_000_000_000_000 + i))).ToList();

        var embed = EventEmbedBuilder.Build(Evt(), signups, NoNames);

        Assert.NotEmpty(embed.Fields);
        Assert.All(embed.Fields, f => Assert.True(f.Value.Length <= EventEmbedBuilder.MaxFieldValueLength, f.Name));
        Assert.EndsWith("more", Field(embed, "✅ Signed Up (300)"));
    }

    [Fact]
    public void Build_BigRaidWhoseFieldsEachFitButTogetherDoNot_IsCutDownUntilTheWholeEmbedFitsDiscordsLimit()
    {
        // Every class column is close to its own 1024-character limit, which adds up to far more than 6000 in all
        var classes = new[] { "warrior", "mage", "priest", "rogue", "hunter", "druid", "paladin", "warlock", "shaman", "monk", "deathknight" };
        var specs = new Dictionary<string, string>
        {
            ["warrior"] = "arms",
            ["mage"] = "fire",
            ["priest"] = "holy",
            ["rogue"] = "subtlety",
            ["hunter"] = "marksman",
            ["druid"] = "balance",
            ["paladin"] = "holy",
            ["warlock"] = "affliction",
            ["shaman"] = "elemental",
            ["monk"] = "windwalker",
            ["deathknight"] = "frost"
        };
        var signups = classes.SelectMany((cls, c) => Enumerable.Range(0, 60).Select(i => Signup((ulong)(c * 100 + i + 1), cls: cls, spec: specs[cls], order: c * 100 + i))).ToList();

        var embed = EventEmbedBuilder.Build(Evt(EventType.WowClassic), signups, id => "SomeLongPlayerName" + id);

        Assert.True(embed.Length <= EventEmbedBuilder.MaxEmbedLength, $"{embed.Length} characters");
        Assert.True(embed.Fields.Length <= 25);
        Assert.NotEmpty(embed.Fields);
        Assert.All(embed.Fields, f => Assert.True(f.Value.Length <= EventEmbedBuilder.MaxFieldValueLength, f.Name));
        Assert.Contains(embed.Fields, f => f.Value.Contains("more"));
    }

    [Fact]
    public void Build_SmallRaid_IsNotCutDownAtAll()
    {
        var signups = Enumerable.Range(1, 5).Select(i => Signup((ulong)i, cls: "mage", spec: "fire", order: i)).ToList();

        var embed = EventEmbedBuilder.Build(Evt(EventType.WowClassic), signups, id => "Player" + id);

        Assert.DoesNotContain(embed.Fields, f => f.Value.Contains("more"));
    }

    [Fact]
    public void JoinTruncated_ItemsThatDoNotFit_AreCutBetweenItemsAndTheRemainderIsCounted()
    {
        var items = new[] { "<@0000000001>", "<@0000000002>", "<@0000000003>", "<@0000000004>", "<@0000000005>" };

        var result = EventEmbedBuilder.JoinTruncated(items, ", ", maxLength: 45);

        Assert.Equal("<@0000000001>, <@0000000002>, … +3 more", result);
    }
}
