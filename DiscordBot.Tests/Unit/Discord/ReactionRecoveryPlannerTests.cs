using DiscordBot.Discord.Constants;
using DiscordBot.Discord.Services;
using DiscordBot.Discord.Services.WoW;
using DiscordBot.Domain.Entities;
using DiscordBot.Domain.Enums;

namespace DiscordBot.Tests.Unit.Discord;

public class ReactionRecoveryPlannerTests
{
    private static readonly string MageEmote = WowEmotes.GetClassEmoteName("mage");

    private static Event Evt(EventType type = EventType.Generic) => new() { EventId = 1, Type = type, Title = "Raid" };

    private static EventSignup Signup(ulong user, SignupStatus status, string? wowClass = null) => new()
    {
        EventId = 1,
        UserId = user,
        Status = status,
        WowClass = wowClass
    };

    private static Dictionary<ulong, List<string>> Reactions(ulong user, params string[] emojis) => new() { [user] = emojis.ToList() };

    [Fact]
    public void Create_SingleSignupReactionTheBotNeverSaw_IsHandledAsIfJustAdded()
    {
        var reactions = Reactions(10, "✅");

        var plan = ReactionRecoveryPlanner.Create(Evt(), Array.Empty<EventSignup>(), reactions);

        Assert.Equal(new[] { new ReactionRecoveryPlanner.Replay(10, "✅") }, plan.Replays);
        Assert.Empty(plan.Rejects);
    }

    [Theory]
    [InlineData("✅", SignupStatus.SignedUp)]
    [InlineData("🤷", SignupStatus.Unsure)]
    [InlineData("❌", SignupStatus.SignedOff)]
    public void Create_SingleReactionThatMatchesTheStoredSignup_NeedsNothing(string emoji, SignupStatus status)
    {
        var signups = new[] { Signup(10, status) };

        var plan = ReactionRecoveryPlanner.Create(Evt(), signups, Reactions(10, emoji));

        Assert.Empty(plan.Replays);
        Assert.Empty(plan.Rejects);
    }

    [Fact]
    public void Create_SingleReactionDifferentFromTheStoredSignup_IsHandledAsIfJustAdded()
    {
        var signups = new[] { Signup(10, SignupStatus.SignedUp) };

        var plan = ReactionRecoveryPlanner.Create(Evt(), signups, Reactions(10, "❌"));

        Assert.Equal(new[] { new ReactionRecoveryPlanner.Replay(10, "❌") }, plan.Replays);
    }

    [Fact]
    public void Create_SeveralSignupReactionsFromOnePerson_AreRemovedInsteadOfGuessingWhichWasMeant()
    {
        var signups = new[] { Signup(11, SignupStatus.Unsure) };
        var reactions = new Dictionary<ulong, List<string>> { [10] = new() { "✅", "❌" }, [11] = new() { "🤷" } };

        var plan = ReactionRecoveryPlanner.Create(Evt(), signups, reactions);

        var reject = Assert.Single(plan.Rejects);
        Assert.Equal(10UL, reject.UserId);
        Assert.Equal(new[] { "✅", "❌" }, reject.Emojis);
        Assert.Empty(plan.Replays);
    }

    [Fact]
    public void Create_PersonWithTheirOldStatusAndANewOne_HandlesTheNewOneAndLeavesTheOldToBeCleanedUp()
    {
        var signups = new[] { Signup(10, SignupStatus.SignedUp) };

        var plan = ReactionRecoveryPlanner.Create(Evt(), signups, Reactions(10, "✅", "❌"));

        Assert.Equal(new[] { new ReactionRecoveryPlanner.Replay(10, "❌") }, plan.Replays);
        Assert.Empty(plan.Rejects);
    }

    [Fact]
    public void Create_TwoNewStatusReactionsAlongsideTheOldOne_AreAllRemovedAsAmbiguous()
    {
        var signups = new[] { Signup(10, SignupStatus.SignedUp) };

        var plan = ReactionRecoveryPlanner.Create(Evt(), signups, Reactions(10, "✅", "❌", "🤷"));

        Assert.Equal(new[] { "✅", "❌", "🤷" }, Assert.Single(plan.Rejects).Emojis);
        Assert.Empty(plan.Replays);
    }

    [Fact]
    public void Create_WowClassReactionWithoutAMatchingSignup_IsHandledSoTheSpecPickerIsSent()
    {
        var evt = Evt(EventType.WowClassic);

        var plan = ReactionRecoveryPlanner.Create(evt, Array.Empty<EventSignup>(), Reactions(10, MageEmote));

        Assert.Equal(new[] { new ReactionRecoveryPlanner.Replay(10, MageEmote) }, plan.Replays);
    }

    [Fact]
    public void Create_WowClassReactionOfSomeoneAlreadySignedUpAsThatClass_NeedsNothing()
    {
        var evt = Evt(EventType.WowClassic);
        var signups = new[] { Signup(10, SignupStatus.SignedUp, "mage") };

        var plan = ReactionRecoveryPlanner.Create(evt, signups, Reactions(10, MageEmote));

        Assert.Empty(plan.Replays);
    }

    [Fact]
    public void Create_ReactionThatIsNotASignupOption_IsPassedOnSoItCanBeRemovedAndDoesNotCountAsASecondChoice()
    {
        var reactions = Reactions(10, "🎉", "✅");

        var plan = ReactionRecoveryPlanner.Create(Evt(), Array.Empty<EventSignup>(), reactions);

        Assert.Equal(new[] { "🎉", "✅" }, plan.Replays.Select(r => r.Emoji));
        Assert.Empty(plan.Rejects);
    }

    [Fact]
    public void Create_ClassReactionOnANonWowEvent_IsNotASignupOption()
    {
        var reactions = Reactions(10, MageEmote, "✅");

        var plan = ReactionRecoveryPlanner.Create(Evt(EventType.Generic), Array.Empty<EventSignup>(), reactions);

        Assert.Empty(plan.Rejects);
        Assert.Contains(plan.Replays, r => r.Emoji == MageEmote);
    }

    [Fact]
    public void Create_ResetSpecReaction_IsReplayedSoTheMemberIsAskedAndIsNotCountedAsAnotherSignupReaction()
    {
        // Signed up as a mage, and clicked reset while the bot was offline
        var signups = new[] { Signup(10, SignupStatus.SignedUp, "mage") };
        var reactions = Reactions(10, MageEmote, SignupEmojis.ResetSpec);

        var plan = ReactionRecoveryPlanner.Create(Evt(EventType.WowClassic), signups, reactions);

        Assert.Equal(new[] { new ReactionRecoveryPlanner.Replay(10, SignupEmojis.ResetSpec) }, plan.Replays);
        Assert.Empty(plan.Rejects);
    }
}
