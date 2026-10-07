using DiscordBot.Application.EventHandlers;
using DiscordBot.Application.Interfaces;
using DiscordBot.Application.Models;
using DiscordBot.Domain.Events;
using DiscordBot.Tests.Support;
using Moq;

namespace DiscordBot.Tests.Unit.Application;

public class MessageLoggingEventHandlerTests
{
    private const ulong GuildId = 20;

    private readonly RecordingMessageSender _sender = new();
    private readonly RecordingMessageLogWriter _writer = new();

    private MessageLoggingEventHandler Create(EchoSettings settings)
    {
        var settingsService = new Mock<ISettingsService>();
        settingsService.Setup(s => s.GetEchoSettingsAsync(GuildId)).ReturnsAsync(settings);

        return new MessageLoggingEventHandler(settingsService.Object, _sender, _writer);
    }

    private static MessageReceivedEvent Message(string content = "hello") => new()
    {
        MessageId = 10,
        GuildId = GuildId,
        ChannelId = 30,
        AuthorId = 40,
        Content = content,
        CreatedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task Handle_Message_IsQueuedForStorageWhateverTheEchoSetting()
    {
        var handler = Create(new EchoSettings(ChannelId: 999, Enabled: false));

        await handler.Handle(Message("hello"));

        var stored = Assert.Single(_writer.Queued);
        Assert.Equal((10UL, GuildId, 30UL, 40UL, "hello"), (stored.MessageId, stored.GuildId, stored.ChannelId, stored.AuthorId, stored.Content));
    }

    [Fact]
    public async Task Handle_EchoOnWithAChannel_PostsATruncatedCopyToThatChannel()
    {
        var handler = Create(new EchoSettings(ChannelId: 999, Enabled: true));

        await handler.Handle(Message(new string('x', 250)));

        var (channelId, content) = Assert.Single(_sender.Sent);
        Assert.Equal(999UL, channelId);
        Assert.StartsWith("<@40> in <#30>: ", content);
        Assert.EndsWith("…", content);
        Assert.True(content.Length < 150);
    }

    [Fact]
    public async Task Handle_EmojiStraddlesTheTruncationPoint_DoesNotSplitTheSurrogatePair()
    {
        // The emoji occupies characters 99 and 100, so cutting at 100 would leave half of it
        var handler = Create(new EchoSettings(ChannelId: 999, Enabled: true));
        var text = new string('x', 99) + "😀" + new string('y', 20);

        await handler.Handle(Message(text));

        var (_, content) = Assert.Single(_sender.Sent);
        Assert.False(content.Any(char.IsHighSurrogate), "a lone high surrogate was left at the cut");
    }

    [Theory]
    [InlineData(999UL, false)]   // echo switched off
    [InlineData(null, true)]     // no channel chosen
    [InlineData(0UL, true)]      // 0 means "cleared"
    public async Task Handle_EchoOffOrNoChannel_PostsNothing(ulong? channelId, bool echoEnabled)
    {
        var handler = Create(new EchoSettings(channelId, echoEnabled));

        await handler.Handle(Message());

        Assert.Empty(_sender.Sent);
    }

    [Fact]
    public async Task Handle_SettingsCannotBeRead_StillQueuesTheMessageAndLetsTheEventBeRetried()
    {
        var settingsService = new Mock<ISettingsService>();
        settingsService.Setup(s => s.GetEchoSettingsAsync(It.IsAny<ulong>())).ThrowsAsync(new Exception("settings store down"));
        var handler = new MessageLoggingEventHandler(settingsService.Object, _sender, _writer);

        var failure = await Record.ExceptionAsync(() => handler.Handle(Message()));

        // The failure is not swallowed (so the consumer retries), but the message is safely queued (storage is idempotent
        Assert.NotNull(failure);
        Assert.Single(_writer.Queued);
        Assert.Empty(_sender.Sent);
    }
}
