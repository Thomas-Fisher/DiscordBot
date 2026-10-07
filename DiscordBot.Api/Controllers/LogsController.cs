using DiscordBot.Contracts;
using DiscordBot.Application.Activity;
using DiscordBot.Application.Interfaces;
using DiscordBot.Application.Models;
using Microsoft.AspNetCore.Mvc;

namespace DiscordBot.Api.Controllers;

public class LogsController : BaseApiController
{
    private readonly IActivityLogService _activityLogs;
    private readonly IGuildDirectory _directory;
    private readonly ISettingsService _settings;

    public LogsController(IGuildAccessService guildAccess, IActivityLogService activityLogs, IGuildDirectory directory, ISettingsService settings)
        : base(guildAccess)
    {
        _activityLogs = activityLogs;
        _directory = directory;
        _settings = settings;
    }

    [HttpGet("{guildId}")]
    public async Task<ActionResult<PaginatedActivityLogsDto>> GetActivityLogs(
        ulong guildId,
        [FromQuery] int? days = IActivityLogService.DefaultDays,
        [FromQuery] string? cursor = null,
        [FromQuery] int pageSize = IActivityLogService.DefaultPageSize)
    {
        return await GetLogsAsync(guildId, null, days, cursor, pageSize);
    }

    [HttpGet("{guildId}/{actionType}")]
    public async Task<ActionResult<PaginatedActivityLogsDto>> GetActivityLogsByAction(
        ulong guildId,
        string actionType,
        [FromQuery] int? days = IActivityLogService.DefaultDays,
        [FromQuery] string? cursor = null,
        [FromQuery] int pageSize = IActivityLogService.DefaultPageSize)
    {
        return await GetLogsAsync(guildId, actionType, days, cursor, pageSize);
    }

    private async Task<ActionResult<PaginatedActivityLogsDto>> GetLogsAsync(ulong guildId, string? actionType, int? days, string? cursor, int pageSize)
    {
        if (await RequireGuildAccessAsync(guildId) is { } denied)
        {
            return denied;
        }

        ActivityLogCursor? after = null;
        if (!string.IsNullOrEmpty(cursor))
        {
            if (!ActivityLogCursor.TryDecode(cursor, out var decoded))
            {
                return BadRequest(Problems.Of(StatusCodes.Status400BadRequest, "Invalid cursor"));
            }

            after = decoded;
        }

        var page = await _activityLogs.GetPageAsync(guildId, actionType, days, after, pageSize, HttpContext.RequestAborted);
        var records = page.Items
            .Select(l => new ActivityRecord(l.ActionType, l.EntityType, l.EntityId, l.GuildId ?? guildId, l.UserId, l.Changes, l.Source))
            .ToList();

        // Everyone a sentence names (the person who acted, and who it was done to) is looked up once for the whole page
        var names = await _directory.GetMemberNamesAsync(guildId, records.SelectMany(ActivityDescriber.UserIds).Distinct().ToList(), HttpContext.RequestAborted);
        var text = new PlainActivityText(id => names.GetValueOrDefault(id) ?? $"User {id}", await _settings.GetTimeZoneAsync(guildId));

        return Ok(new PaginatedActivityLogsDto
        {
            Logs = page.Items.Select((l, index) => new ActivityLogDto
            {
                LogId = l.LogId,
                Timestamp = l.Timestamp,
                ActionType = l.ActionType,
                EntityType = l.EntityType,
                EntityId = l.EntityId,
                UserId = l.UserId,
                UserDisplayName = l.UserId == null ? null : names.GetValueOrDefault(l.UserId.Value),
                Summary = ActivityDescriber.Describe(records[index], text).ToString(),
                Source = l.Source,
                Changes = l.Changes
            }).ToList(),
            PageSize = page.PageSize,
            NextCursor = page.Next?.Encode()
        });
    }
}
