using Discord;
using Discord.Interactions;
using DiscordBot.Discord.Constants;
using DiscordBot.Discord.Services;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Discord.SlashCommands;

/// <summary>The two commands that start creating an event. What happens next is <see cref="CreateEventFlow"/>.</summary>
[RequireContext(ContextType.Guild)]
public class CreateEventModule : ModuleBase
{
    private readonly EventPermissionService _permissions;
    private readonly ILogger<CreateEventModule> _logger;

    public CreateEventModule(EventPermissionService permissions, ILogger<CreateEventModule> logger)
    {
        _permissions = permissions;
        _logger = logger;
    }

    [DefaultMemberPermissions(GuildPermission.ManageGuild)]
    [SlashCommand("createevent", "Create a scheduled event")]
    public Task CreateEventAsync()
    {
        return AskForEventTypeAsync(InteractionCustomIds.SelectEventTypePrefix, "🎯 **Create Event** - What type of event is this?");
    }

    [DefaultMemberPermissions(GuildPermission.ManageGuild)]
    [SlashCommand("createrecurringevent", "Create a recurring scheduled event")]
    public Task CreateRecurringEventAsync()
    {
        return AskForEventTypeAsync(InteractionCustomIds.SelectRecurringEventTypePrefix, "🔄 **Create Recurring Event** - Select event type");
    }

    private async Task AskForEventTypeAsync(string customIdPrefix, string prompt)
    {
        if (Context.User is not IGuildUser guildUser)
        {
            await RespondAsync(UserMessages.ErrorProcessingRequest, ephemeral: true);
            return;
        }

        if (!_permissions.CanCreateEvent(guildUser))
        {
            _logger.LogWarning("User {UserId} attempted to create an event without permission in guild {GuildId}", Context.User.Id, Context.Guild.Id);
            await RespondAsync(UserMessages.InsufficientPermissions, ephemeral: true);
            return;
        }

        var typeMenu = new SelectMenuBuilder()
            .WithCustomId($"{customIdPrefix}{Context.User.Id}")
            .WithPlaceholder("Select event type");

        foreach (var option in EventTypeOptions.All)
        {
            typeMenu.AddOption(option.Label, option.Value, option.Description);
        }

        await RespondAsync(prompt, components: new ComponentBuilder().WithSelectMenu(typeMenu).Build(), ephemeral: true);
    }
}
