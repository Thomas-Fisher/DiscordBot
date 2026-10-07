using Discord;
using Discord.WebSocket;
using DiscordBot.Application.Interfaces;
using DiscordBot.Discord.Services;
using DiscordBot.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DiscordBot.Tests.Unit.Discord;

public class DiscordEventAnnouncerTests
{
    /// <summary>Holds queued work until the test runs it, so what happens before and after the queue gets to it can be told apart.</summary>
    private sealed class DeferredQueue : IBackgroundTaskQueue
    {
        public List<Func<CancellationToken, Task>> Work { get; } = new();

        public bool Enqueue(string name, Func<CancellationToken, Task> work)
        {
            Work.Add(work);

            return true;
        }
    }

    [Fact]
    public async Task PostToChannel_NewWowEvent_HandsBackTheMessageBeforeAnyReactionIsAddedSoTheEventCanBeLinkedToItFirst()
    {
        var reactions = new List<string>();
        var message = new Mock<IUserMessage>();
        message.SetupGet(m => m.Id).Returns(42);
        message.Setup(m => m.AddReactionAsync(It.IsAny<IEmote>(), It.IsAny<RequestOptions>()))
            .Returns<IEmote, RequestOptions>((emote, _) =>
            {
                reactions.Add(emote.Name);
                return Task.CompletedTask;
            });
        var channel = new Mock<IMessageChannel>();
        channel.Setup(c => c.SendMessageAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<Embed>(), It.IsAny<RequestOptions>(), It.IsAny<AllowedMentions>(),
                It.IsAny<MessageReference>(), It.IsAny<MessageComponent>(), It.IsAny<ISticker[]>(), It.IsAny<Embed[]>(), It.IsAny<MessageFlags>(), It.IsAny<PollProperties>()))
            .ReturnsAsync(message.Object);
        var queue = new DeferredQueue();
        using var client = new DiscordSocketClient();
        var announcer = new DiscordEventAnnouncer(client, queue, NullLogger<DiscordEventAnnouncer>.Instance);

        var posted = await announcer.PostToChannelAsync(channel.Object, TestEvents.Wow());

        Assert.Equal(42UL, posted.Id);
        Assert.Empty(reactions);
        await Assert.Single(queue.Work)(CancellationToken.None);
        Assert.NotEmpty(reactions);
    }
}
