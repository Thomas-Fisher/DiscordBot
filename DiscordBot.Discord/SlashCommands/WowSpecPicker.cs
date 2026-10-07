using Discord;
using Discord.Interactions;
using DiscordBot.Application.Errors;
using DiscordBot.Application.Interfaces;
using DiscordBot.Application.Models;
using DiscordBot.Discord.Constants;
using DiscordBot.Discord.Services;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Discord.SlashCommands;

/// <summary>The second half of signing up to a WoW event: after clicking a class emote, the member picks a spec from the menu the bot sent them.</summary>
public class WowSpecPicker : ModuleBase
{
    private readonly IEventService _events;
    private readonly ISignupService _signups;
    private readonly IRememberedSpecRepository _rememberedSpecs;
    private readonly ISpecPrompter _prompter;
    private readonly InteractionState _state;
    private readonly ILogger<WowSpecPicker> _logger;

    public WowSpecPicker(
        IEventService events,
        ISignupService signups,
        IRememberedSpecRepository rememberedSpecs,
        ISpecPrompter prompter,
        InteractionState state,
        ILogger<WowSpecPicker> logger)
    {
        _events = events;
        _signups = signups;
        _rememberedSpecs = rememberedSpecs;
        _prompter = prompter;
        _state = state;
        _logger = logger;
    }

    [ComponentInteraction($"{InteractionCustomIds.WowSpecSelectPrefix}*")]
    public async Task HandleWowSpecSelectionAsync()
    {
        // Signing up and removing the class reaction talk to the database and Discord, which can outlast the 3-second window
        await DeferAsync(ephemeral: true);

        if (Context.Interaction is not IComponentInteraction componentInteraction)
        {
            await ReplyAsync(UserMessages.InvalidInteractionType);
            return;
        }

        // Parse customId: wow_spec_select_{userId}_{messageId}
        var customId = componentInteraction.Data.CustomId;
        var parts = customId.Split('_');
        if (parts.Length < 5 || !ulong.TryParse(parts[3], out var userId) || !ulong.TryParse(parts[4], out var messageId))
        {
            await ReplyAsync(UserMessages.InvalidSpecSelectionFormat);
            return;
        }

        if (Context.User.Id != userId)
        {
            await ReplyAsync(UserMessages.SpecSelectionNotForYou);
            return;
        }

        var context = _state.GetSpecSelection(userId, messageId);
        if (context == null)
        {
            await ReplyAsync(UserMessages.SpecSelectionExpired);
            return;
        }

        var selectedSpec = componentInteraction.Data.Values.FirstOrDefault();
        if (string.IsNullOrEmpty(selectedSpec))
        {
            await ReplyAsync(UserMessages.PleaseSelectSpec);
            return;
        }

        var evt = await _events.FindAsync(context.EventId);
        if (evt == null)
        {
            await ReplyAsync(UserMessages.EventNotFound);
            return;
        }

        var result = await _signups.SignUpAsync(evt, userId, context.ClassName, selectedSpec, ActivityContext.Discord(userId));

        if (!result.Success)
        {
            _logger.LogWarning("Spec selection rejected: user {UserId}, {Class} {Spec}: {Error}", userId, context.ClassName, selectedSpec, result.Error);
            await ReplyAsync(result.Error == SignupErrors.EventNotOpen
                ? "❌ This event is no longer open for signups."
                : $"❌ {result.Error!.Message}");
            return;
        }

        // Next time they sign up with this class (on any event of this type in the server) this spec is used without asking
        await _rememberedSpecs.SaveAsync(evt.GuildId, userId, evt.Type, context.ClassName, selectedSpec);

        await _prompter.RemoveClassReactionAsync(evt, userId, context.ClassName);

        var order = result.Value ?? 0;
        await ReplyAsync($"✅ Signed up as **{context.ClassName}** - **{selectedSpec}** (#{order})");

        _state.RemoveSpecSelection(userId, messageId);

        _logger.LogInformation("User {UserId} signed up with spec {Spec} for class {Class} order {Order}", userId, selectedSpec, context.ClassName, order);
    }
}
