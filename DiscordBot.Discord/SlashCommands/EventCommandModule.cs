using Discord;
using DiscordBot.Application.Interfaces;
using DiscordBot.Discord.Constants;
using DiscordBot.Discord.Services;
using DiscordBot.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Discord.SlashCommands;

/// <summary>The shared shape of commands that act on one existing event: acknowledge, find it, check it exists, is in this server and the caller may manage it, then run. Failures are logged and reported.</summary>
public abstract class EventCommandModule : ModuleBase
{
    private readonly EventPermissionService _permissions;

    protected EventCommandModule(IEventService events, EventPermissionService permissions, ILogger logger)
    {
        Events = events;
        _permissions = permissions;
        Logger = logger;
    }

    protected IEventService Events { get; }

    protected ILogger Logger { get; }

    protected Task RunOnEventAsync(ulong eventId, string action, Func<Event, Task<string>> command)
    {
        return RunOnEventAsync(eventId, action, async evt => await ReplyAsync(await command(evt)));
    }

    protected async Task RunOnEventAsync(ulong eventId, string action, Func<Event, Task> command)
    {
        await DeferAsync(ephemeral: true);

        try
        {
            var evt = await Events.FindAsync(eventId);

            var refusal = RefusalFor(evt);
            if (refusal != null)
            {
                if (refusal == UserMessages.NotEventCreator)
                {
                    Logger.LogWarning("User {UserId} attempted to {Action} event {EventId} without permission", Context.User.Id, action, eventId);
                }

                await ReplyAsync(refusal);
                return;
            }

            await command(evt!);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error trying to {Action} event {EventId}", action, eventId);
            await ReplyAsync(UserMessages.ErrorProcessingRequest);
        }
    }

    /// <summary>Why the caller cannot act on this event, or null when they can: it must exist, belong to this server, and be theirs or theirs to manage.</summary>
    private string? RefusalFor(Event? evt)
    {
        if (Context.Guild == null)
        {
            return UserMessages.GuildOnly;
        }

        if (evt == null)
        {
            return UserMessages.EventNotFound;
        }

        if (evt.GuildId != Context.Guild.Id)
        {
            return UserMessages.EventNotInGuild;
        }

        return Context.User is IGuildUser user && _permissions.CanManageEvent(evt, user) ? null : UserMessages.NotEventCreator;
    }
}
