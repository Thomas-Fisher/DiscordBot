using DiscordBot.Domain.Common;
using DiscordBot.Domain.Enums;
using DiscordBot.Contracts;
using DiscordBot.Application;
using DiscordBot.Application.Interfaces;
using DiscordBot.Application.Models;
using Microsoft.AspNetCore.Mvc;

namespace DiscordBot.Api.Controllers;

public class SettingsController : BaseApiController
{
    private readonly ISettingsService _settingsService;
    private readonly IActivityLogger _activityLogger;
    private readonly IGuildDirectory _directory;

    public SettingsController(IGuildAccessService guildAccess, ISettingsService settingsService, IActivityLogger activityLogger, IGuildDirectory directory)
        : base(guildAccess)
    {
        _settingsService = settingsService;
        _activityLogger = activityLogger;
        _directory = directory;
    }

    [HttpGet("guild/{guildId}")]
    public async Task<IActionResult> GetGuildSettings(ulong guildId)
    {
        if (await RequireGuildAccessAsync(guildId) is { } denied)
        {
            return denied;
        }

        return Ok(await ReadAsync(guildId));
    }

    [HttpGet("timezones")]
    public ActionResult<IReadOnlyList<string>> GetTimeZones()
    {
        return Ok(GuildTimeZone.AvailableIds());
    }

    [HttpPost("guild/{guildId}")]
    public async Task<IActionResult> UpdateGuildSettings(ulong guildId, [FromBody] UpdateSettingsRequest request)
    {
        if (await RequireGuildAccessAsync(guildId) is { } denied)
        {
            return denied;
        }

        // Checked before anything is saved, so a bad zone changes nothing
        GuildTimeZone? zone = null;
        if (request.TimeZone != null)
        {
            if (!GuildTimeZone.TryFind(request.TimeZone, out var found))
            {
                return BadRequest(Problems.Of(StatusCodes.Status400BadRequest, $"'{request.TimeZone}' is not a known time zone. Use a name such as Europe/London."));
            }

            zone = found;
        }

        List<AuditCategory>? categories = null;
        if (request.DisabledAuditCategories != null)
        {
            categories = new List<AuditCategory>();
            foreach (var name in request.DisabledAuditCategories)
            {
                if (!Enum.TryParse<AuditCategory>(name, ignoreCase: true, out var category) || !Enum.IsDefined(category))
                {
                    return BadRequest(Problems.Of(StatusCodes.Status400BadRequest, $"'{name}' is not an audit category. Use {string.Join(", ", Enum.GetNames<AuditCategory>())}."));
                }

                categories.Add(category);
            }
        }

        // A channel that is not one of this server's text channels would make the log echo fail with nobody told. NoChannel clears it
        if (request.EchoChannelId is { } channelId && channelId != UpdateSettingsRequest.NoChannel
            && (await _directory.GetTextChannelsAsync(guildId, HttpContext.RequestAborted))?.Any(c => c.Id == channelId) != true)
        {
            return BadRequest(Problems.Of(StatusCodes.Status400BadRequest, "That is not a text channel in this server."));
        }

        await _settingsService.SaveEchoSettingsAsync(guildId, request.EchoChannelId, request.EchoEnabled);

        if (zone != null)
        {
            await _settingsService.SaveTimeZoneAsync(guildId, zone);
        }

        if (categories != null)
        {
            await _settingsService.SaveDisabledAuditCategoriesAsync(guildId, categories);
        }

        // One row per save, listing what was changed
        var changes = new Dictionary<string, object>();
        if (categories != null)
        {
            changes[SettingKeys.DisabledAuditCategories] = categories.Select(c => c.ToString()).Order().ToList();
        }

        if (zone != null)
        {
            changes[SettingKeys.TimeZone] = zone.Id;
        }

        if (request.EchoChannelId.HasValue)
        {
            changes[SettingKeys.EchoChannel] = request.EchoChannelId.Value;
        }

        if (request.EchoEnabled.HasValue)
        {
            changes[SettingKeys.EchoEnabled] = request.EchoEnabled.Value;
        }

        if (changes.Count > 0)
        {
            await _activityLogger.LogAsync(new ActivityEntry(ActivityActionType.Updated, ActivityEntity.GuildSettings, guildId, guildId, CurrentUserId, ActivityOrigin.Api, new { changes }));
        }

        return Ok(await ReadAsync(guildId));
    }

    private async Task<GuildSettingsDto> ReadAsync(ulong guildId)
    {
        return ToDto(guildId, await _settingsService.GetEchoSettingsAsync(guildId), await _settingsService.GetTimeZoneAsync(guildId),
            await _settingsService.GetDisabledAuditCategoriesAsync(guildId));
    }

    private static GuildSettingsDto ToDto(ulong guildId, EchoSettings settings, GuildTimeZone zone, IReadOnlySet<AuditCategory> disabledCategories)
    {
        return new GuildSettingsDto
        {
            GuildId = guildId,
            EchoChannelId = settings.ChannelId,
            EchoEnabled = settings.Enabled,
            EchoActive = settings.ActiveChannelId.HasValue,
            TimeZone = zone.Id,
            DisabledAuditCategories = disabledCategories.Select(c => c.ToString()).Order().ToList()
        };
    }
}
