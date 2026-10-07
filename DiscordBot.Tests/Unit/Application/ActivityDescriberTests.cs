using DiscordBot.Application.Activity;
using DiscordBot.Discord.Presentation;
using DiscordBot.Domain.Common;

namespace DiscordBot.Tests.Unit.Application;

public class ActivityDescriberTests
{
    private const ulong Guild = 10;

    private static readonly IActivityText Plain = new PlainActivityText(id => $"Player{id}", GuildTimeZone.Utc);
    private static readonly IActivityText InDiscord = new DiscordActivityText(Guild);

    private static ActivityRecord Record(string action, string entity = "Event", ulong? userId = 1, object? changes = null, ulong? entityId = 99, string source = "Discord") =>
        new(action, entity, entityId, Guild, userId, changes == null ? null : System.Text.Json.JsonSerializer.Serialize(changes), source);

    private static string AsPlain(ActivityRecord record) => ActivityDescriber.Describe(record, Plain).ToString();

    private static string AsDiscord(ActivityRecord record) => ActivityDescriber.Describe(record, InDiscord).ToString();

    [Fact]
    public void Describe_JoiningAVoiceChannel_NamesThePersonAndTheChannel()
    {
        var record = Record("VoiceStateChanged", "VoiceState", changes: new { kind = "joined", channelId = 55UL, channelName = "General" });

        Assert.Equal("🔊 Player1 joined #General", AsPlain(record));
        Assert.Equal("🔊 <@1> **joined** <#55>", AsDiscord(record));
    }

    [Fact]
    public void Describe_LeavingAndMovingBetweenVoiceChannels_SaysWhichChannels()
    {
        var left = Record("VoiceStateChanged", "VoiceState", changes: new { kind = "left", channelId = 55UL, channelName = "General" });
        var moved = Record("VoiceStateChanged", "VoiceState", changes: new { kind = "moved", channelId = 56UL, channelName = "Raid", fromChannelId = 55UL, fromChannelName = "General" });

        Assert.Equal("🔇 Player1 left #General", AsPlain(left));
        Assert.Equal("🔀 Player1 moved from #General to #Raid", AsPlain(moved));
        Assert.Equal("🔀 <@1> **moved** from <#55> to <#56>", AsDiscord(moved));
    }

    [Theory]
    [InlineData("none", "General", "🔊 Player1 joined #General")]
    [InlineData("General", "none", "🔇 Player1 left #General")]
    [InlineData("General", "Raid", "🔀 Player1 moved from #General to #Raid")]
    public void Describe_VoiceEntryFromBeforeJoinsAndLeavesWereRecorded_WorksItOutFromTheTwoChannelNames(string before, string after, string expected)
    {
        var record = Record("VoiceStateChanged", "VoiceState", changes: new { beforeChannel = before, afterChannel = after });

        Assert.Equal(expected, AsPlain(record));
    }

    [Fact]
    public void Describe_ReactionWithACustomEmote_ShowsTheEmoteInDiscordAndItsNameOnTheDashboard()
    {
        var record = Record("ReactionAdded", "Reaction", changes: new { emoji = "<:mage_fire:123>", emojiName = "mage_fire", channelId = 55UL, channelName = "raids" });

        Assert.Equal("😀 Player1 reacted :mage_fire: to a message in #raids", AsPlain(record));
        Assert.Equal("😀 <@1> **reacted** <:mage_fire:123> to a message in <#55> ([jump](https://discord.com/channels/10/55/99))", AsDiscord(record));
    }

    [Fact]
    public void Describe_ReactionOnAnEventMessage_NamesTheEvent()
    {
        var added = Record("ReactionAdded", "Reaction", changes: new { emoji = "✅", channelId = 55UL, channelName = "raids", eventTitle = "SoO Raid" });
        var removed = Record("ReactionRemoved", "Reaction", changes: new { emoji = "✅", channelId = 55UL, channelName = "raids", eventTitle = "SoO Raid" });

        Assert.Equal("😀 Player1 reacted ✅ on the event \"SoO Raid\"", AsPlain(added));
        Assert.Equal("😶 Player1 removed their ✅ reaction from the event \"SoO Raid\"", AsPlain(removed));
    }

    [Fact]
    public void Describe_EditedMessage_ShowsWhereTheNewAndTheOriginalText()
    {
        var record = Record("MessageUpdated", "Message", changes: new { channelId = 55UL, channelName = "general", before = "see you at 8", after = "see you at 9" });

        Assert.Equal("✏️ Player1 edited a message in #general: \"see you at 9\" (originally \"see you at 8\")", AsPlain(record));
        Assert.Equal("✏️ <@1> **edited** a message in <#55> ([jump](https://discord.com/channels/10/55/99)): \"see you at 9\" (originally \"see you at 8\")", AsDiscord(record));
    }

    [Fact]
    public void Describe_EditedMessageFromBeforeDetailsWereRecorded_StillReadsAsAnEdit()
    {
        Assert.Equal("✏️ Player1 edited a message", AsPlain(Record("MessageUpdated", "Message")));
    }

    [Fact]
    public void Describe_DeletedMessageWithItsAuthor_NamesTheAuthorTheChannelAndAPreview()
    {
        var record = Record("MessageDeleted", "Message", userId: null, changes: new { channelId = 55UL, channelName = "general", authorId = 7UL, content = "hello there" });

        Assert.Equal("🗑️ A message by Player7 was deleted in #general: \"hello there\"", AsPlain(record));
        Assert.Equal("🗑️ A message by <@7> was **deleted** in <#55>: \"hello there\"", AsDiscord(record));
    }

    [Fact]
    public void Describe_MessageDeletedByAModerator_SaysWho()
    {
        var record = Record("MessageDeleted", "Message", userId: 2, changes: new { channelId = 55UL, channelName = "general", authorId = 7UL });

        Assert.Equal("🗑️ A message by Player7 was deleted in #general, by Player2", AsPlain(record));
    }

    [Fact]
    public void Describe_MessageDeletedByItsAuthor_DoesNotRepeatThePersonAsTheDeleter()
    {
        var record = Record("MessageDeleted", "Message", userId: 7, changes: new { channelName = "general", authorId = 7UL });

        Assert.Equal("🗑️ A message by Player7 was deleted in #general", AsPlain(record));
    }

    [Fact]
    public void Describe_QuotedTextFromSomeoneElse_CannotFormatTheDiscordLine()
    {
        var record = Record("MessageUpdated", "Message", changes: new { channelName = "general", after = "**bold** ||spoiler|| <@1> `code`" });

        var line = AsDiscord(record);

        Assert.Contains("\"\\*\\*bold\\*\\* \\|\\|spoiler\\|\\| \\<@1\\> \\`code\\`\"", line);
    }

    [Fact]
    public void Describe_MembersJoiningAndLeaving_SaysSo()
    {
        Assert.Equal("📥 Player5 joined the server", AsPlain(Record("UserJoined", "User", userId: 5)));
        Assert.Equal("📤 <@5> **left** the server", AsDiscord(Record("UserLeft", "User", userId: 5)));
    }

    [Fact]
    public void Describe_ChannelsAndRoles_AreDescribedWithoutAnActor()
    {
        Assert.Equal("🆕 Channel #raids was created", AsPlain(Record("ChannelCreated", "Channel", userId: null, changes: new { name = "raids" }, entityId: 55)));
        Assert.Equal("🆕 Channel <#55> was **created**", AsDiscord(Record("ChannelCreated", "Channel", userId: null, changes: new { name = "raids" }, entityId: 55)));
        Assert.Equal("🗑️ Channel #raids was deleted", AsPlain(Record("ChannelDeleted", "Channel", userId: null, changes: new { name = "raids" })));
        Assert.Equal("🗑️ Role \"Raiders\" was deleted", AsPlain(Record("RoleDeleted", "Role", userId: null, changes: new { name = "Raiders" })));
        Assert.Equal("🆕 Role *Raiders* was **created**", AsDiscord(Record("RoleCreated", "Role", userId: null, changes: new { name = "Raiders" })));
    }

    [Fact]
    public void Describe_CreatedEvent_SaysWhatAndWhenAndWhetherItRepeats()
    {
        var record = Record("Created", changes: new { eventTitle = "SoO Raid", scheduledTime = new DateTime(2026, 10, 8, 19, 0, 0, DateTimeKind.Utc), recurrence = "EveryFriday" });

        Assert.Equal("📅 Player1 created \"SoO Raid\" for Oct 8, 19:00 (repeats: every friday)", AsPlain(record));
        Assert.Contains("<t:1791486000:f>", AsDiscord(record));
    }

    [Fact]
    public void Describe_UpdatedEvent_ListsWhichDetailsChanged()
    {
        var record = Record("Updated", changes: new { eventTitle = "SoO Raid", title = "SoO Raid", description = (string?)null, scheduledTime = DateTime.UtcNow, durationMinutes = 180 });

        Assert.Equal("✏️ Player1 updated \"SoO Raid\": title, start time, duration", AsPlain(record));
    }

    [Fact]
    public void Describe_EventMovedToAnotherChannel_SaysFromAndTo()
    {
        var record = Record("Updated", changes: new { eventTitle = "SoO Raid", movedFromChannel = 55UL, movedToChannel = 56UL });

        Assert.Equal("📍 <@1> **moved** *SoO Raid* from <#55> to <#56>", AsDiscord(record));
    }

    [Fact]
    public void Describe_CancelledDeletedAndCompletedEvents_ReadNaturally()
    {
        Assert.Equal("🚫 Player1 cancelled \"SoO Raid\"", AsPlain(Record("Cancelled", changes: new { eventTitle = "SoO Raid" })));
        Assert.Equal("🗑️ Player1 deleted \"SoO Raid\"", AsPlain(Record("Deleted", changes: new { eventTitle = "SoO Raid" })));
        Assert.Equal("🏁 \"SoO Raid\" has finished", AsPlain(Record("Completed", userId: null, changes: new { eventTitle = "SoO Raid" }, source: "System")));
        Assert.Equal("🚫 Player1 cancelled the whole series of \"SoO Raid\" (4 events)", AsPlain(Record("CancelledRecurring", changes: new { eventTitle = "SoO Raid", cancelledCount = 4 })));
    }

    [Fact]
    public void Describe_NextOccurrenceCreatedByTheBot_DoesNotNameAnyone()
    {
        var record = Record("CreatedRecurring", userId: null, changes: new { eventTitle = "SoO Raid", nextScheduledTime = new DateTime(2026, 10, 15, 19, 0, 0, DateTimeKind.Utc) }, source: "Recurring");

        Assert.Equal("🔄 Next occurrence of \"SoO Raid\" scheduled for Oct 15, 19:00", AsPlain(record));
    }

    [Fact]
    public void Describe_SigningUpYourself_ShowsTheClassSpecAndPlace()
    {
        var record = Record("SignedUp", userId: 1, changes: new { userId = 1UL, eventTitle = "SoO Raid", details = new { wowClass = "deathknight", wowSpec = "blood", order = 3 } });

        Assert.Equal("✅ Player1 signed up to \"SoO Raid\" as Death Knight (Blood), #3", AsPlain(record));
        Assert.Equal("✅ <@1> **signed up** to *SoO Raid* as Death Knight (Blood), #3", AsDiscord(record));
    }

    [Fact]
    public void Describe_AnAdminChangingSomeoneElsesSignup_NamesBothPeople()
    {
        var up = Record("SignedUp", userId: 2, changes: new { userId = 1UL, eventTitle = "SoO Raid", details = (object?)null });
        var off = Record("SignedOff", userId: 2, changes: new { userId = 1UL, eventTitle = "SoO Raid" });
        var unsure = Record("MarkedUnsure", userId: 2, changes: new { userId = 1UL, eventTitle = "SoO Raid" });

        Assert.Equal("✅ Player2 signed up Player1 to \"SoO Raid\"", AsPlain(up));
        Assert.Equal("❌ Player2 removed Player1 from \"SoO Raid\"", AsPlain(off));
        Assert.Equal("🤷 Player2 marked Player1 as unsure for \"SoO Raid\"", AsPlain(unsure));
    }

    [Fact]
    public void Describe_SigningOffAndBeingUnsureYourself_ReadsInTheFirstPerson()
    {
        Assert.Equal("❌ Player1 signed off from \"SoO Raid\"", AsPlain(Record("SignedOff", userId: 1, changes: new { userId = 1UL, eventTitle = "SoO Raid" })));
        Assert.Equal("🤷 Player1 is unsure about \"SoO Raid\"", AsPlain(Record("MarkedUnsure", userId: 1, changes: new { userId = 1UL, eventTitle = "SoO Raid" })));
    }

    [Fact]
    public void Describe_EventEntriesWithoutATitle_StillMakeSense()
    {
        Assert.Equal("🚫 Player1 cancelled an event", AsPlain(Record("Cancelled")));
    }

    [Fact]
    public void Describe_SettingsChangedFromTheDashboardOrACommand_ListsWhatChanged()
    {
        var dashboard = Record("Updated", "GuildSettings", changes: new { changes = new Dictionary<string, object> { ["time_zone"] = "Europe/London", ["echo_channel"] = 55UL, ["echo_enabled"] = true } });
        var command = Record("Updated", "GuildSettings", changes: new { setting = "time_zone", value = "Europe/London" });

        Assert.Equal("⚙️ Player1 changed the server settings: time zone to Europe/London, log channel to a channel, log echo on", AsPlain(dashboard));
        Assert.Equal("⚙️ Player1 changed the server settings: time zone to Europe/London", AsPlain(command));
    }

    [Fact]
    public void Describe_RosterSet_SaysHowManyPeopleInHowManyGroups()
    {
        var record = Record("RosterUpdated", changes: new { eventTitle = "SoO Raid", people = 10, groups = 2 });

        Assert.Equal("📋 Player1 set the roster for \"SoO Raid\": 10 people in 2 groups", AsPlain(record));
        Assert.Equal("📋 <@1> **set the roster** for *SoO Raid*: 1 person in 1 group", AsDiscord(Record("RosterUpdated", changes: new { eventTitle = "SoO Raid", people = 1, groups = 1 })));
    }

    [Theory]
    [InlineData("Roster", 10, "📣 Player1 pinged 10 people in the roster for \"SoO Raid\"")]
    [InlineData("Standby", 1, "📣 Player1 pinged 1 person on standby for \"SoO Raid\"")]
    [InlineData("NoResponse", 3, "📣 Player1 pinged 3 people who had not responded for \"SoO Raid\"")]
    public void Describe_PingSent_SaysWhoWasPingedAndHowMany(string target, int people, string expected)
    {
        var record = Record("PingSent", changes: new { eventTitle = "SoO Raid", target, people });

        Assert.Equal(expected, AsPlain(record));
    }

    [Fact]
    public void Describe_ActionNobodyWroteWordingFor_FallsBackToAGenericSentence()
    {
        Assert.Equal("📝 Player1 something new (Event)", AsPlain(Record("SomethingNew")));
    }

    [Fact]
    public void Describe_NoPersonOnAnEntry_SaysTheBotForAutomaticChangesAndSomeoneOtherwise()
    {
        Assert.Equal("🚫 The bot cancelled an event", AsPlain(Record("Cancelled", userId: null, source: "System")));
        Assert.Equal("🚫 Someone cancelled an event", AsPlain(Record("Cancelled", userId: null, source: "Discord")));
    }

    [Fact]
    public void Describe_DamagedChangesJson_IsIgnoredRatherThanFailing()
    {
        var record = new ActivityRecord("VoiceStateChanged", "VoiceState", 5, Guild, 1, "{not json", "Discord");

        Assert.Equal("🔊 Player1 joined a channel", AsPlain(record));
    }

    [Fact]
    public void UserIds_EntryNamingSeveralPeople_ListsEachOnce()
    {
        var record = Record("SignedUp", userId: 2, changes: new { userId = 1UL, authorId = 2UL });

        Assert.Equal(new ulong[] { 1, 2 }, ActivityDescriber.UserIds(record).Order());
    }

    [Fact]
    public void Time_OnTheDashboard_IsShownInTheServersTimeZone()
    {
        var london = new PlainActivityText(id => $"Player{id}", GuildTimeZone.FindOrUtc("Europe/London"));
        var record = Record("CreatedRecurring", userId: null, changes: new { eventTitle = "SoO Raid", nextScheduledTime = new DateTime(2026, 7, 1, 19, 0, 0, DateTimeKind.Utc) }, source: "Recurring");

        Assert.Equal("🔄 Next occurrence of \"SoO Raid\" scheduled for Jul 1, 20:00", ActivityDescriber.Describe(record, london).ToString());
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("   \n ", null)]
    [InlineData("hello   there\nfriend", "hello there friend")]
    public void Of_TextPreview_CollapsesWhiteSpaceAndSaysNothingForEmptyText(string? text, string? expected)
    {
        Assert.Equal(expected, TextPreview.Of(text));
    }

    [Fact]
    public void Of_TextPreview_CutsLongTextWithAnEllipsisAndNeverInTheMiddleOfAnEmoji()
    {
        var text = new string('a', 98) + "🎉🎉";

        var preview = TextPreview.Of(text)!;

        Assert.EndsWith("…", preview);
        Assert.Equal(new string('a', 98) + "…", preview);
        Assert.True(preview.Length <= TextPreview.DefaultLength);
    }
}
