using DiscordBot.Application;
using Discord;
using Discord.WebSocket;
using DiscordBot.Application.Interfaces;
using DiscordBot.Discord.Constants;
using DiscordBot.Discord.Services;
using DiscordBot.Domain.Entities;
using DiscordBot.Domain.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Discord.DiscordEventHandlers;

/// <summary>
/// Catches up on reactions added while the bot was offline, which the gateway never replays. Runs each time the bot is ready
/// (see <see cref="ReactionRecoveryPlanner"/>).
/// </summary>
public class MissedReactionRecoveryService : IGatewayHandler
{
    private const int ReactionUsersPageSize = 100;

    // Ready fires after every reconnect. A short drop loses no reactions worth a full scan of every event message, so a scan is
    // only repeated once this much time has passed
    private static readonly TimeSpan MinimumTimeBetweenScans = TimeSpan.FromMinutes(15);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IBackgroundTaskQueue _backgroundQueue;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MissedReactionRecoveryService> _logger;
    private DiscordSocketClient _client = null!;
    private DateTime? _lastScanAt;

    public MissedReactionRecoveryService(
        IServiceScopeFactory scopeFactory,
        [FromKeyedServices(BackgroundQueues.Maintenance)] IBackgroundTaskQueue backgroundQueue,
        TimeProvider timeProvider,
        ILogger<MissedReactionRecoveryService> logger)
    {
        _timeProvider = timeProvider;
        _scopeFactory = scopeFactory;
        _backgroundQueue = backgroundQueue;
        _logger = logger;
    }

    public void Attach(DiscordSocketClient client)
    {
        _client = client;
        client.Ready += () =>
        {
            _backgroundQueue.Enqueue(nameof(MissedReactionRecoveryService), _ => RecoverAsync());
            return Task.CompletedTask;
        };
    }

    private async Task RecoverAsync()
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (_lastScanAt is { } last && now - last < MinimumTimeBetweenScans)
        {
            _logger.LogDebug("Skipping the missed reaction scan: the last one was {Minutes:0} minutes ago", (now - last).TotalMinutes);
            return;
        }

        _lastScanAt = now;

        using var scope = _scopeFactory.CreateScope();
        var events = scope.ServiceProvider.GetRequiredService<IEventService>();
        var signups = scope.ServiceProvider.GetRequiredService<ISignupService>();

        var replayed = 0;
        var rejected = 0;

        foreach (var guild in _client.Guilds)
        {
            foreach (var evt in await events.GetUpcomingAsync(guild.Id))
            {
                if (evt.MessageId == null || evt.ChannelId == null)
                {
                    continue;
                }

                try
                {
                    var plan = await PlanAsync(evt, signups);
                    if (plan == null)
                    {
                        continue;
                    }

                    await ApplyAsync(evt, plan, scope.ServiceProvider.GetRequiredService<IOutbox>());
                    replayed += plan.Replays.Count;
                    rejected += plan.Rejects.Count;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Could not recover missed reactions for event {EventId}", evt.EventId);
                }
            }
        }

        _logger.LogInformation("Missed reaction recovery finished: {Replayed} reactions handled, {Rejected} people asked to react again", replayed, rejected);
    }

    private async Task<ReactionRecoveryPlanner.Plan?> PlanAsync(Event evt, ISignupService signups)
    {
        if (_client.GetChannel(evt.ChannelId!.Value) is not IMessageChannel channel
            || await channel.GetMessageAsync(evt.MessageId!.Value) is not { } message)
        {
            return null;
        }

        var emojisByUser = new Dictionary<ulong, List<string>>();

        foreach (var (emote, metadata) in message.Reactions)
        {
            // The bot's own reaction is the only one, so nobody else has used this emoji
            if (metadata.ReactionCount - (metadata.IsMe ? 1 : 0) <= 0)
            {
                continue;
            }

            await foreach (var page in message.GetReactionUsersAsync(emote, ReactionUsersPageSize))
            {
                foreach (var user in page.Where(u => !u.IsBot))
                {
                    if (!emojisByUser.TryGetValue(user.Id, out var emojis))
                    {
                        emojis = new List<string>();
                        emojisByUser[user.Id] = emojis;
                    }

                    emojis.Add(emote.ToString()!);
                }
            }
        }

        return ReactionRecoveryPlanner.Create(evt, (await signups.GetSignupsAsync(evt.EventId)), emojisByUser);
    }

    private async Task ApplyAsync(Event evt, ReactionRecoveryPlanner.Plan plan, IOutbox outbox)
    {
        foreach (var reject in plan.Rejects)
        {
            await RejectAsync(evt, reject);
        }

        foreach (var replay in plan.Replays)
        {
            await outbox.AddAsync(new ReactionAddedEvent
            {
                MessageId = evt.MessageId!.Value,
                ChannelId = evt.ChannelId!.Value,
                UserId = replay.UserId,
                Emoji = replay.Emoji
            });
        }
    }

    private async Task RejectAsync(Event evt, ReactionRecoveryPlanner.Reject reject)
    {
        var channel = (IMessageChannel)_client.GetChannel(evt.ChannelId!.Value);
        var message = await channel.GetMessageAsync(evt.MessageId!.Value);

        foreach (var emoji in reject.Emojis)
        {
            IEmote emote = Emote.TryParse(emoji, out var custom) ? custom : new Emoji(emoji);
            await message.RemoveReactionAsync(emote, reject.UserId);
        }

        _logger.LogInformation("Removed {Count} reactions by user {UserId} from event {EventId}: several sign-up reactions while offline",
            reject.Emojis.Count, reject.UserId, evt.EventId);

        try
        {
            var user = await _client.GetUserAsync(reject.UserId);
            if (user != null)
            {
                await user.SendMessageAsync(string.Format(UserMessages.ReactionsNotProcessed, evt.Title));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not tell user {UserId} that their reactions on event {EventId} were not processed", reject.UserId, evt.EventId);
        }
    }
}
