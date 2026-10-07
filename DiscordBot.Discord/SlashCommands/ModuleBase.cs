using Discord;
using Discord.Interactions;

namespace DiscordBot.Discord.SlashCommands;

public abstract class ModuleBase : InteractionModuleBase<SocketInteractionContext>
{
    /// <summary>
    /// Answers the interaction, or follows up when it has already been acknowledged (deferred, or answered earlier), so an
    /// error handler never fails with "already responded".
    /// </summary>
    protected async Task ReplyAsync(string text, bool ephemeral = true)
    {
        if (Context.Interaction.HasResponded)
        {
            await FollowupAsync(text, ephemeral: ephemeral);
        }
        else
        {
            await RespondAsync(text, ephemeral: ephemeral);
        }
    }

    /// <summary>The first option picked in the select menu that caused this interaction, or null when the interaction is not a menu choice.</summary>
    protected string? SelectedValue()
    {
        return Context.Interaction is IComponentInteraction component ? component.Data.Values.FirstOrDefault() : null;
    }
}
