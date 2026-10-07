using DiscordBot.Application.Activity;
using DiscordBot.Application.Interfaces;
using DiscordBot.Domain.Entities;
using DiscordBot.Domain.Events;

namespace DiscordBot.Application.EventHandlers;

// Storing is idempotent (by message id); a redelivery could echo the message again, which is accepted for the cost of a lookup per message
[NaturallyIdempotent]
public class MessageLoggingEventHandler : IEventHandler<MessageReceivedEvent>
{
    private readonly ISettingsService _settings;
    private readonly IDiscordMessageSender _discordMessageSender;
    private readonly IMessageLogWriter _messageLogWriter;

    public MessageLoggingEventHandler(
        ISettingsService settings,
        IDiscordMessageSender discordMessageSender,
        IMessageLogWriter messageLogWriter)
    {
        _settings = settings;
        _discordMessageSender = discordMessageSender;
        _messageLogWriter = messageLogWriter;
    }

    public async Task Handle(MessageReceivedEvent @event)
    {
        // Messages are always stored. Storage is buffered and written in batches; nothing here waits on the database.
        _messageLogWriter.Enqueue(new MessageLog
        {
            MessageId = @event.MessageId,
            GuildId = @event.GuildId,
            ChannelId = @event.ChannelId,
            AuthorId = @event.AuthorId,
            Content = @event.Content,
            CreatedAt = @event.CreatedAt,
            LoggedAt = DateTime.UtcNow,
            IsDeleted = false
        });

        // The message is already queued, so a failure here (the sender throws for anything retrying could fix) just retries the event
        var echoChannelId = (await _settings.GetEchoSettingsAsync(@event.GuildId)).ActiveChannelId;
        if (echoChannelId.HasValue)
        {
            await _discordMessageSender.SendMessageAsync(echoChannelId.Value, FormatLogMessage(@event));
        }
    }

    private static string FormatLogMessage(MessageReceivedEvent @event)
    {
        var content = TextPreview.Of(@event.Content) ?? "";
        return $"<@{@event.AuthorId}> in <#{@event.ChannelId}>: {content}";
    }
}
