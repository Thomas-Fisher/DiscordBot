using System.Net;
using Discord.Net;
using Discord.WebSocket;
using DiscordBot.Application.Errors;
using DiscordBot.Application.Interfaces;
using DiscordBot.Application.Models;
using DiscordBot.Discord;
using DiscordBot.Discord.DomainEventHandlers;
using DiscordBot.Discord.Constants;
using DiscordBot.Discord.Services;
using DiscordBot.Discord.Services.WoW;
using DiscordBot.Domain.Entities;
using DiscordBot.Domain.Enums;
using DiscordBot.Domain.Events;
using DiscordBot.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DiscordBot.Tests.Unit.Discord;

public class EventSignupHandlerTests
{
    private readonly Mock<IEventService> _events = new();
    private readonly Mock<ISignupService> _signups = new();
    private readonly Mock<ISignupRepository> _signupRepository = new();
    private readonly Mock<IRememberedSpecRepository> _remembered = new();
    private readonly Mock<ISpecPrompter> _prompter = new();
    private readonly Mock<IBackgroundTaskQueue> _queue = new();
    private readonly EventSignupHandler _handler;

    public EventSignupHandlerTests()
    {
        _queue.Setup(q => q.Enqueue(It.IsAny<string>(), It.IsAny<Func<CancellationToken, Task>>())).Returns(true);

        _handler = new EventSignupHandler(
            _events.Object,
            _signups.Object,
            _signupRepository.Object,
            _remembered.Object,
            _prompter.Object,
            new DiscordSocketClient(),
            _queue.Object,
            NullLogger<EventSignupHandler>.Instance);
    }

    private static ReactionAddedEvent Reaction(string emoji) => new() { MessageId = 1001, ChannelId = 501, UserId = 42, Emoji = emoji };

    [Fact]
    public async Task Handle_SignupReactionWhileTheDatabaseIsFailing_ThrowsSoTheEventIsRetriedInsteadOfLost()
    {
        _events.Setup(e => e.FindByMessageAsync(1001)).ThrowsAsync(new InvalidOperationException("database down"));

        var failure = await Record.ExceptionAsync(() => _handler.Handle(Reaction("✅")));

        Assert.IsType<InvalidOperationException>(failure);
    }

    [Fact]
    public async Task Handle_SignupServiceFails_ThrowsSoTheEventIsRetried()
    {
        var evt = TestEvents.Scheduled();
        _events.Setup(e => e.FindByMessageAsync(1001)).ReturnsAsync(evt);
        _signups.Setup(s => s.SignUpAsync(evt, 42, null, null, It.IsAny<ActivityContext>())).ThrowsAsync(new TimeoutException());

        await Assert.ThrowsAsync<TimeoutException>(() => _handler.Handle(Reaction("✅")));
    }

    [Fact]
    public async Task Handle_ReactionOnAMessageThatIsNotAnEvent_DoesNothing()
    {
        _events.Setup(e => e.FindByMessageAsync(1001)).ReturnsAsync((Event?)null);

        await _handler.Handle(Reaction("✅"));

        _signups.VerifyNoOtherCalls();
        _queue.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_SignupTheRulesRefuse_DoesNotFailAndStillClearsTheReaction()
    {
        // For example the event has ended
        var evt = TestEvents.Scheduled();
        _events.Setup(e => e.FindByMessageAsync(1001)).ReturnsAsync(evt);
        _signups.Setup(s => s.SignUpAsync(evt, 42, null, null, It.IsAny<ActivityContext>())).ReturnsAsync(Result<int?>.Fail(SignupErrors.EventNotOpen));

        await _handler.Handle(Reaction("✅"));

        // The click is dealt with either way, so the reaction must not be left looking accepted
        _queue.Verify(q => q.Enqueue(It.IsAny<string>(), It.IsAny<Func<CancellationToken, Task>>()), Times.Once);
    }

    [Theory]
    [InlineData("✅")]
    [InlineData("❌")]
    [InlineData("🤷")]
    public async Task Handle_StatusReaction_AppliesItAndSchedulesRemovalOfTheReaction(string emoji)
    {
        var evt = TestEvents.Scheduled();
        _events.Setup(e => e.FindByMessageAsync(1001)).ReturnsAsync(evt);
        _signups.Setup(s => s.SignUpAsync(evt, 42, null, null, It.IsAny<ActivityContext>())).ReturnsAsync(Result<int?>.Ok(null));
        _signups.Setup(s => s.SignOffAsync(evt, 42, It.IsAny<ActivityContext>())).ReturnsAsync(Result<int?>.Ok(null));
        _signups.Setup(s => s.MarkUnsureAsync(evt, 42, It.IsAny<ActivityContext>())).ReturnsAsync(Result<int?>.Ok(null));

        await _handler.Handle(Reaction(emoji));

        _queue.Verify(q => q.Enqueue(It.IsAny<string>(), It.IsAny<Func<CancellationToken, Task>>()), Times.Once);
    }

    private static readonly string MageEmote = WowEmotes.GetClassEmoteName("mage");

    [Fact]
    public async Task Handle_ClassReactionWithARememberedSpec_SignsUpWithItAndDoesNotAsk()
    {
        var evt = TestEvents.Wow();
        _events.Setup(e => e.FindByMessageAsync(1001)).ReturnsAsync(evt);
        _remembered.Setup(r => r.FindAsync(evt.GuildId, 42, evt.Type, "mage")).ReturnsAsync("fire");
        _signups.Setup(s => s.SignUpAsync(evt, 42, "mage", "fire", It.IsAny<ActivityContext>())).ReturnsAsync(Result<int?>.Ok(1));

        await _handler.Handle(Reaction(MageEmote));

        // The class reaction is cleared, as it is after choosing from the menu
        _signups.Verify(s => s.SignUpAsync(evt, 42, "mage", "fire", It.IsAny<ActivityContext>()), Times.Once);
        _prompter.Verify(p => p.SendPickerAsync(It.IsAny<Event>(), It.IsAny<ulong>(), It.IsAny<string>()), Times.Never);
        _queue.Verify(q => q.Enqueue(It.IsAny<string>(), It.IsAny<Func<CancellationToken, Task>>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ClassReactionWithNothingRemembered_AsksForTheSpec()
    {
        var evt = TestEvents.Wow();
        _events.Setup(e => e.FindByMessageAsync(1001)).ReturnsAsync(evt);
        _remembered.Setup(r => r.FindAsync(evt.GuildId, 42, evt.Type, "mage")).ReturnsAsync((string?)null);

        await _handler.Handle(Reaction(MageEmote));

        _prompter.Verify(p => p.SendPickerAsync(evt, 42, "mage"), Times.Once);
        _signups.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_RememberedSpecTheClassNoLongerHas_ForgetsItAndAsksAgain()
    {
        var evt = TestEvents.Wow();
        _events.Setup(e => e.FindByMessageAsync(1001)).ReturnsAsync(evt);
        _remembered.Setup(r => r.FindAsync(evt.GuildId, 42, evt.Type, "mage")).ReturnsAsync("gone");
        _signups.Setup(s => s.SignUpAsync(evt, 42, "mage", "gone", It.IsAny<ActivityContext>())).ReturnsAsync(Result<int?>.Fail(SignupErrors.InvalidClassOrSpec));

        await _handler.Handle(Reaction(MageEmote));

        _remembered.Verify(r => r.DeleteAsync(evt.GuildId, 42, evt.Type, "mage"), Times.Once);
        _prompter.Verify(p => p.SendPickerAsync(evt, 42, "mage"), Times.Once);
    }

    [Fact]
    public async Task Handle_ResetSpecWhileSignedUpWithAClass_AsksForTheSpecOfThatClassAndClearsTheReaction()
    {
        var evt = TestEvents.Wow();
        _events.Setup(e => e.FindByMessageAsync(1001)).ReturnsAsync(evt);
        _signupRepository.Setup(s => s.GetEventSignupAsync(evt.EventId, 42))
            .ReturnsAsync(new EventSignup { EventId = evt.EventId, UserId = 42, Status = SignupStatus.SignedUp, WowClass = "mage", WowSpec = "fire" });

        await _handler.Handle(Reaction(SignupEmojis.ResetSpec));

        _prompter.Verify(p => p.SendPickerAsync(evt, 42, "mage"), Times.Once);
        _queue.Verify(q => q.Enqueue(It.IsAny<string>(), It.IsAny<Func<CancellationToken, Task>>()), Times.Once);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(SignupStatus.SignedOff, "mage")]
    [InlineData(SignupStatus.Unsure, null)]
    public async Task Handle_ResetSpecWithoutBeingSignedUpWithAClass_IsAStrayClickAndOnlyClearsTheReaction(SignupStatus? status, string? wowClass)
    {
        var evt = TestEvents.Wow();
        _events.Setup(e => e.FindByMessageAsync(1001)).ReturnsAsync(evt);
        _signupRepository.Setup(s => s.GetEventSignupAsync(evt.EventId, 42)).ReturnsAsync(status == null
            ? null
            : new EventSignup { EventId = evt.EventId, UserId = 42, Status = status.Value, WowClass = wowClass });

        await _handler.Handle(Reaction(SignupEmojis.ResetSpec));

        _prompter.Verify(p => p.SendPickerAsync(It.IsAny<Event>(), It.IsAny<ulong>(), It.IsAny<string>()), Times.Never);
        _queue.Verify(q => q.Enqueue(It.IsAny<string>(), It.IsAny<Func<CancellationToken, Task>>()), Times.Once);
    }

    private static HttpException Failure(HttpStatusCode code) => new(code, null!);

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]      // the bot lacks the permission
    [InlineData(HttpStatusCode.NotFound)]       // the channel or message is gone
    public void IsPermanent_FailureNoRetryCanFix_IsTrue(HttpStatusCode code)
    {
        Assert.True(DiscordErrors.IsPermanent(Failure(code)));
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    public void IsPermanent_FailureThatMayPass_IsFalse(HttpStatusCode code)
    {
        Assert.False(DiscordErrors.IsPermanent(Failure(code)));
    }

    [Fact]
    public void IsPermanent_AnyOtherException_IsFalse()
    {
        Assert.False(DiscordErrors.IsPermanent(new InvalidOperationException()));
    }
}
