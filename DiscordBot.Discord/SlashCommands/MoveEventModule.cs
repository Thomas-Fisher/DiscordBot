using Discord;
using Discord.Interactions;
using DiscordBot.Application.Interfaces;
using DiscordBot.Application.Models;
using DiscordBot.Discord.Constants;
using DiscordBot.Discord.Services;
using DiscordBot.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Discord.SlashCommands;

[RequireContext(ContextType.Guild)]
public class MoveEventModule : EventCommandModule
{
    private readonly ISignupService _signups;
    private readonly EventMessageMover _mover;

    public MoveEventModule(IEventService events, ISignupService signups, EventMessageMover mover, EventPermissionService permissions, ILogger<MoveEventModule> logger)
        : base(events, permissions, logger)
    {
        _signups = signups;
        _mover = mover;
    }

    [SlashCommand("moveeventhere", "Move an event to this channel by event ID")]
    public Task MoveEventHereAsync([Summary(description: "The unique ID of the event to move")] ulong eventId)
    {
        return RunOnEventAsync(eventId, "move", async evt =>
            Context.Channel is ITextChannel target ? await MoveAsync(evt, target) : "❌ Events can only be moved to a text channel.");
    }

    [SlashCommand("moveevent", "Move an event to a selected channel")]
    public Task MoveEventAsync([Summary(description: "The unique ID of the event to move")] ulong eventId)
    {
        return RunOnEventAsync(eventId, "move", async (Event evt) =>
        {
            var channelMenu = new SelectMenuBuilder()
                .WithCustomId($"{InteractionCustomIds.MoveEventPrefix}{eventId}")
                .WithPlaceholder("Select destination channel")
                .WithType(ComponentType.ChannelSelect)
                .WithChannelTypes(ChannelType.Text)
                .WithMinValues(1)
                .WithMaxValues(1);

            await FollowupAsync($"Select a channel to move **{evt.Title}** to:", components: new ComponentBuilder().WithSelectMenu(channelMenu).Build(), ephemeral: true);
        });
    }

    [ComponentInteraction($"{InteractionCustomIds.MoveEventPrefix}*")]
    public async Task HandleMoveEventSelectAsync()
    {
        if (Context.Interaction is not IComponentInteraction component
            || !ulong.TryParse(component.Data.CustomId[InteractionCustomIds.MoveEventPrefix.Length..], out var eventId))
        {
            await RespondAsync("❌ Invalid event ID format.", ephemeral: true);
            return;
        }

        var selectedChannelId = component.Data.Channels?.FirstOrDefault()?.Id ?? 0UL;

        await RunOnEventAsync(eventId, "move", async evt =>
        {
            if (selectedChannelId == 0)
            {
                return "❌ No channel selected.";
            }

            return Context.Guild.GetTextChannel(selectedChannelId) is { } target
                ? await MoveAsync(evt, target)
                : "❌ That channel isn't a text channel in this server.";
        });
    }

    private async Task<string> MoveAsync(Event evt, ITextChannel target)
    {
        var result = await _mover.MoveAsync(evt, target, Events, _signups, ActivityContext.Discord(Context.User.Id));

        return result.Success ? $"✅ Event `{evt.Title}` has been moved to {target.Mention}. Signups were kept." : result.Error!;
    }
}
