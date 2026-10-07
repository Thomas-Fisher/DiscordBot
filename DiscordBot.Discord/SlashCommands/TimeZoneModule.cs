using Discord;
using Discord.Interactions;
using DiscordBot.Application;
using DiscordBot.Application.Interfaces;
using DiscordBot.Application.Models;
using DiscordBot.Discord.Services;
using DiscordBot.Domain.Common;
using DiscordBot.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Discord.SlashCommands;

[RequireContext(ContextType.Guild)]
public class TimeZoneModule : ModuleBase
{
    private readonly ISettingsService _settings;
    private readonly IActivityLogger _activityLogger;
    private readonly EventPermissionService _permissions;
    private readonly ILogger<TimeZoneModule> _logger;

    public TimeZoneModule(ISettingsService settings, IActivityLogger activityLogger, EventPermissionService permissions, ILogger<TimeZoneModule> logger)
    {
        _settings = settings;
        _activityLogger = activityLogger;
        _permissions = permissions;
        _logger = logger;
    }

    [SlashCommand("timezone", "Show this server's time zone, or set it (times you type for events are read in it)")]
    public async Task HandleTimeZoneAsync(
        [Summary(description: "The new time zone, for example Europe/London (leave empty to just show the current one)")]
        [Autocomplete(typeof(TimeZoneAutocompleteHandler))] string? zone = null)
    {
        await DeferAsync(ephemeral: true);

        var current = await _settings.GetTimeZoneAsync(Context.Guild.Id);

        if (string.IsNullOrWhiteSpace(zone))
        {
            await FollowupAsync($"🕐 This server's time zone is **{current.Id}**. Times you type for events are read in it.", ephemeral: true);
            return;
        }

        if (Context.User is not IGuildUser user || !_permissions.CanCreateEvent(user))
        {
            await FollowupAsync("❌ Only the server owner and people with the Administrator or Manage Server permission can change the time zone.", ephemeral: true);
            return;
        }

        if (!GuildTimeZone.TryFind(zone, out var chosen))
        {
            await FollowupAsync($"❌ '{zone}' is not a time zone I know. Start typing a name such as Europe/London and pick one from the list.", ephemeral: true);
            return;
        }

        await _settings.SaveTimeZoneAsync(Context.Guild.Id, chosen);
        await _activityLogger.LogAsync(new ActivityEntry(ActivityActionType.Updated, ActivityEntity.GuildSettings, Context.Guild.Id, Context.Guild.Id, Context.User.Id,
            ActivityOrigin.Discord, new { setting = SettingKeys.TimeZone, value = chosen.Id }));

        _logger.LogInformation("Guild {GuildId} time zone set to {TimeZone} by {UserId}", Context.Guild.Id, chosen.Id, Context.User.Id);

        var now = DateTime.UtcNow;
        await FollowupAsync(
            $"✅ Time zone set to **{chosen.Id}** (it is now {chosen.ToLocal(now):HH:mm} there). Events already scheduled keep their time; recurring ones follow this zone's clock from now on.",
            ephemeral: true);
    }
}
