using Discord;
using Discord.Rest;
using Discord.WebSocket;
using DiscordBot.Application;
using DiscordBot.Application.Activity;
using DiscordBot.Application.Interfaces;
using DiscordBot.Application.Models;
using DiscordBot.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Discord.DiscordEventHandlers;

public class DiscordActivityAuditor : IGatewayHandler
{
    // Discord writes a deletion to the audit log a moment after it sends the event
    private static readonly TimeSpan AuditLogDelay = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan AuditLogWindow = TimeSpan.FromSeconds(30);

    /// <summary>What a row records, worked out when it is written. Null from the lookup means there is nothing worth recording.</summary>
    private sealed record AuditDetails(ulong? UserId, object? Changes);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IBackgroundTaskQueue _backgroundQueue;
    private readonly IBackgroundTaskQueue _slowQueue;
    private readonly ILogger<DiscordActivityAuditor> _logger;
    private DiscordSocketClient _client = null!;

    public DiscordActivityAuditor(
        IServiceScopeFactory scopeFactory,
        IBackgroundTaskQueue backgroundQueue,
        [FromKeyedServices(BackgroundQueues.Maintenance)] IBackgroundTaskQueue slowQueue,
        ILogger<DiscordActivityAuditor> logger)
    {
        _backgroundQueue = backgroundQueue;
        _slowQueue = slowQueue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void Attach(DiscordSocketClient client)
    {
        _client = client;

        client.UserJoined += user =>
            LogAsync(ActivityActionType.UserJoined, ActivityEntity.User, user.Id, user.Guild.Id, user.Id);

        client.UserLeft += (guild, user) =>
            LogAsync(ActivityActionType.UserLeft, ActivityEntity.User, user.Id, guild.Id, user.Id);

        client.ChannelCreated += channel => channel is SocketGuildChannel guildChannel
            ? LogAsync(ActivityActionType.ChannelCreated, ActivityEntity.Channel, channel.Id, guildChannel.Guild.Id, null, new { name = guildChannel.Name })
            : Task.CompletedTask;

        client.ChannelDestroyed += channel => channel is SocketGuildChannel guildChannel
            ? LogAsync(ActivityActionType.ChannelDeleted, ActivityEntity.Channel, channel.Id, guildChannel.Guild.Id, null, new { name = guildChannel.Name })
            : Task.CompletedTask;

        client.RoleCreated += role =>
            LogAsync(ActivityActionType.RoleCreated, ActivityEntity.Role, role.Id, role.Guild.Id, null, new { name = role.Name });

        client.RoleDeleted += role =>
            LogAsync(ActivityActionType.RoleDeleted, ActivityEntity.Role, role.Id, role.Guild.Id, null, new { name = role.Name });

        client.MessageUpdated += (before, after, channel) =>
        {
            // The bot edits its own event announcements constantly; only people's edits are interesting
            if (after.Author?.IsBot == true || channel is not SocketGuildChannel guildChannel)
            {
                return Task.CompletedTask;
            }

            // Discord also sends this when a link's preview loads or a message is pinned; only a real edit carries an edit time
            if (after.EditedTimestamp == null)
            {
                return Task.CompletedTask;
            }

            var messageId = after.Id;
            var authorId = after.Author?.Id;
            var newText = after.Content;

            return LogAsync(ActivityActionType.MessageUpdated, ActivityEntity.Message, messageId, guildChannel.Guild.Id, async services =>
            {
                var stored = await services.GetRequiredService<IMessageLogRepository>().FindAsync(messageId);

                return new AuditDetails(authorId, new
                {
                    channelId = guildChannel.Id,
                    channelName = guildChannel.Name,
                    before = TextPreview.Of(stored?.Content),
                    after = TextPreview.Of(newText)
                });
            });
        };

        client.MessageDeleted += async (message, channel) =>
        {
            var resolved = channel.HasValue ? channel.Value : await channel.GetOrDownloadAsync();
            if (resolved is not SocketGuildChannel guildChannel)
            {
                return;
            }

            var messageId = message.Id;
            var cached = message.HasValue ? message.Value : null;

            await LogAsync(ActivityActionType.MessageDeleted, ActivityEntity.Message, messageId, guildChannel.Guild.Id, async services =>
            {
                var stored = await services.GetRequiredService<IMessageLogRepository>().FindAsync(messageId);
                var authorId = stored?.AuthorId ?? cached?.Author.Id;
                var deletedBy = await FindDeleterAsync(guildChannel, authorId);

                // Without the author or the deleter there is nothing to say: the bot clearing away an announcement, or a message from before it was here
                if ((authorId == null && deletedBy == null) || authorId == _client.CurrentUser?.Id)
                {
                    return null;
                }

                return new AuditDetails(deletedBy, new
                {
                    channelId = guildChannel.Id,
                    channelName = guildChannel.Name,
                    authorId,
                    content = TextPreview.Of(stored?.Content ?? cached?.Content)
                });
            }, slow: true);

            await MarkMessageDeletedAsync(messageId);
        };

        client.UserVoiceStateUpdated += (user, before, after) =>
        {
            var left = before.VoiceChannel;
            var joined = after.VoiceChannel;

            // Muting, deafening and streaming also arrive here, with the same channel on both sides. Only changing channel is recorded
            if (left?.Id == joined?.Id)
            {
                return Task.CompletedTask;
            }

            var channel = joined ?? left!;
            object changes = left == null
                ? new { kind = "joined", channelId = channel.Id, channelName = channel.Name }
                : joined == null
                    ? new { kind = "left", channelId = channel.Id, channelName = channel.Name }
                    : new { kind = "moved", channelId = joined.Id, channelName = joined.Name, fromChannelId = left.Id, fromChannelName = left.Name };

            return LogAsync(ActivityActionType.VoiceStateChanged, ActivityEntity.VoiceState, channel.Id, channel.Guild.Id, user.Id, changes);
        };

        client.ReactionAdded += (message, channel, reaction) => LogReactionAsync(ActivityActionType.ReactionAdded, message.Id, channel, reaction);
        client.ReactionRemoved += (message, channel, reaction) => LogReactionAsync(ActivityActionType.ReactionRemoved, message.Id, channel, reaction);
    }

    private async Task LogReactionAsync(ActivityActionType action, ulong messageId, Cacheable<IMessageChannel, ulong> channel, SocketReaction reaction)
    {
        if (reaction.User.IsSpecified && reaction.User.Value.IsBot)
        {
            return;
        }

        var emoji = reaction.Emote.ToString();
        if (string.IsNullOrEmpty(emoji))
        {
            return;
        }

        var resolved = channel.HasValue ? channel.Value : await channel.GetOrDownloadAsync();
        if (resolved is not SocketGuildChannel guildChannel)
        {
            return;
        }

        var emojiName = reaction.Emote is Emote custom ? custom.Name : null;
        var userId = reaction.UserId;

        await LogAsync(action, ActivityEntity.Reaction, messageId, guildChannel.Guild.Id, async services =>
        {
            var announced = await services.GetRequiredService<IEventService>().FindByMessageAsync(messageId);

            // The bot clears the signup reactions on an event message as it handles them; recording that as people removing them would be noise
            if (announced != null && action == ActivityActionType.ReactionRemoved)
            {
                return null;
            }

            return new AuditDetails(userId, new
            {
                emoji,
                emojiName,
                channelId = guildChannel.Id,
                channelName = guildChannel.Name,
                eventTitle = announced?.Title
            });
        });
    }

    /// <summary>
    /// Who deleted the message, when it was someone other than its author. Discord only says so in the audit log (and only for a
    /// moderator deleting somebody else's message), which needs the View Audit Log permission. Null when it cannot be told.
    /// </summary>
    private async Task<ulong?> FindDeleterAsync(SocketGuildChannel channel, ulong? authorId)
    {
        try
        {
            if (!channel.Guild.CurrentUser.GuildPermissions.ViewAuditLog)
            {
                return null;
            }

            await Task.Delay(AuditLogDelay);

            var entries = await channel.Guild.GetAuditLogsAsync(10, actionType: ActionType.MessageDeleted).FlattenAsync();
            var since = DateTimeOffset.UtcNow - AuditLogWindow;

            return entries
                .Where(e => e.CreatedAt >= since && e.Data is MessageDeleteAuditLogData data && data.ChannelId == channel.Id && (authorId == null || data.Target.Id == authorId))
                .OrderByDescending(e => e.CreatedAt)
                .FirstOrDefault()?.User?.Id;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read the audit log of guild {GuildId} to find who deleted a message", channel.Guild.Id);

            return null;
        }
    }

    private async Task MarkMessageDeletedAsync(ulong messageId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IMessageLogRepository>().MarkDeletedAsync(messageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error flagging message {MessageId} as deleted", messageId);
        }
    }

    // Gateway callbacks must return quickly (Discord.Net warns when they block), and voice and reaction events are frequent, so the
    // audit row is written from the background queue
    private Task LogAsync(ActivityActionType action, ActivityEntity entity, ulong entityId, ulong guildId, ulong? userId, object? changes = null)
    {
        return LogAsync(action, entity, entityId, guildId, _ => Task.FromResult<AuditDetails?>(new AuditDetails(userId, changes)));
    }

    // details works out what to record when the row is written, where the database is available; the slow lane is for lookups that wait on Discord
    private Task LogAsync(ActivityActionType action, ActivityEntity entity, ulong entityId, ulong guildId, Func<IServiceProvider, Task<AuditDetails?>> details, bool slow = false)
    {
        (slow ? _slowQueue : _backgroundQueue).Enqueue(action.ToString(), _ => WriteAsync(action, entity, entityId, guildId, details));

        return Task.CompletedTask;
    }

    private async Task WriteAsync(ActivityActionType action, ActivityEntity entity, ulong entityId, ulong guildId, Func<IServiceProvider, Task<AuditDetails?>> details)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();

            if (AuditCategories.Of(action) is { } category
                && (await scope.ServiceProvider.GetRequiredService<ISettingsService>().GetDisabledAuditCategoriesAsync(guildId)).Contains(category))
            {
                return;
            }

            if (await details(scope.ServiceProvider) is not { } recorded)
            {
                return;
            }

            await scope.ServiceProvider.GetRequiredService<IActivityLogger>()
                .LogAsync(new ActivityEntry(action, entity, entityId, guildId, recorded.UserId, ActivityOrigin.Discord, recorded.Changes));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording {Action} in the activity log", action);
        }
    }
}
