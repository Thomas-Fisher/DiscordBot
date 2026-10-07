using DiscordBot.Contracts;
using DiscordBot.Application.Errors;
using DiscordBot.Application.Interfaces;
using DiscordBot.Application.Models;
using DiscordBot.Domain.Common;
using DiscordBot.Domain.Entities;
using DiscordBot.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace DiscordBot.Api.Controllers;

public class EventsController : BaseApiController
{
    private readonly IEventService _eventService;
    private readonly ISignupService _signupService;
    private readonly IEventCreator _eventCreator;
    private readonly IEventPingService _pings;
    private readonly IGuildDirectory _directory;

    public EventsController(IGuildAccessService guildAccess, IEventService eventService, ISignupService signupService, IEventCreator eventCreator, IEventPingService pings, IGuildDirectory directory)
        : base(guildAccess)
    {
        _eventService = eventService;
        _signupService = signupService;
        _eventCreator = eventCreator;
        _pings = pings;
        _directory = directory;
    }

    private ActivityContext Actor => ActivityContext.Api(CurrentUserId);

    [HttpGet("byid/{eventId}")]
    public async Task<ActionResult<EventDetailDto>> GetEventById(ulong eventId)
    {
        var (evt, denied) = await LoadEventAsync(_eventService, eventId);
        if (evt == null)
        {
            return denied!;
        }

        var signups = await _signupService.GetSignupsAsync(eventId, HttpContext.RequestAborted);
        var names = await _directory.GetMemberNamesAsync(evt.GuildId, signups.Select(s => s.UserId), HttpContext.RequestAborted);

        var response = new EventDetailDto
        {
            EventId = evt.EventId,
            GuildId = evt.GuildId,
            Title = evt.Title,
            Description = evt.Description,
            ScheduledTime = evt.ScheduledTime,
            Type = evt.Type.ToString(),
            Status = evt.Status.ToString(),
            CreatedBy = evt.CreatedBy,
            CreatedAt = evt.CreatedAt,
            ChannelId = evt.ChannelId ?? 0,
            MessageId = evt.MessageId ?? 0,
            Signups = signups.Select(s => new SignupDto
            {
                UserId = s.UserId,
                DisplayName = names.GetValueOrDefault(s.UserId),
                SignedUpAt = s.SignedUpAt
            }).ToList(),
            IsRecurring = evt.IsRecurring,
            RecurrencePattern = evt.RecurrencePattern?.ToString(),
            ParentRecurringEventId = evt.ParentRecurringEventId,
            DurationMinutes = evt.DurationMinutes,
            ReminderMinutes = evt.ReminderMinutesBefore,
            MentionRoleIds = evt.MentionRoleIds.ToList()
        };

        return Ok(response);
    }

    [HttpGet("{guildId}")]
    public async Task<ActionResult<IEnumerable<EventDto>>> GetGuildEvents(ulong guildId)
    {
        if (await RequireGuildAccessAsync(guildId) is { } denied)
        {
            return denied;
        }

        var events = await _eventService.GetActiveAsync(guildId, HttpContext.RequestAborted);
        var counts = await _signupService.GetCountsAsync(events.Select(e => e.EventId).ToList(), HttpContext.RequestAborted);
        var eventDtos = events
            .Select(e => new EventDto
            {
                EventId = e.EventId,
                Title = e.Title,
                Description = e.Description,
                ScheduledTime = e.ScheduledTime,
                Type = e.Type.ToString(),
                Status = e.Status.ToString(),
                CreatedBy = e.CreatedBy,
                CreatedAt = e.CreatedAt,
                IsRecurring = e.IsRecurring,
                RecurrencePattern = e.RecurrencePattern?.ToString(),
                DurationMinutes = e.DurationMinutes,
                SignedUpCount = counts.GetValueOrDefault(e.EventId)?.SignedUp ?? 0,
                UnsureCount = counts.GetValueOrDefault(e.EventId)?.Unsure ?? 0,
                MentionRoleIds = e.MentionRoleIds.ToList()
            }).ToList();

        return Ok(eventDtos);
    }

    [HttpPost("{guildId}")]
    public async Task<ActionResult<EventCreatedDto>> CreateEvent(ulong guildId, [FromBody] CreateEventRequest request)
    {
        if (await RequireGuildAccessAsync(guildId) is { } denied)
        {
            return denied;
        }

        if (CurrentUserId is not { } userId)
        {
            return Unauthorized(Problems.Of(StatusCodes.Status401Unauthorized, "User ID not found in token"));
        }

        if (!Enum.TryParse<EventType>(request.Type, ignoreCase: true, out var type) || !Enum.IsDefined(type))
        {
            return ProblemFor(EventErrors.UnknownType);
        }

        RecurrencePattern? recurrence = null;
        if (!string.IsNullOrWhiteSpace(request.Recurrence))
        {
            if (!Enum.TryParse<RecurrencePattern>(request.Recurrence, ignoreCase: true, out var pattern) || !Enum.IsDefined(pattern))
            {
                return ProblemFor(EventErrors.UnknownRecurrence);
            }

            recurrence = pattern;
        }

        var channels = await _directory.GetTextChannelsAsync(guildId, HttpContext.RequestAborted);
        if (channels == null || channels.All(c => c.Id != request.ChannelId))
        {
            return ProblemFor(EventErrors.UnknownChannel);
        }

        var newEvent = new NewEventRequest(
            guildId, userId, request.Title, request.Description, request.ScheduledTime, type, request.ReminderMinutes, recurrence,
            request.DurationMinutes, request.MentionRoleIds);

        var created = await _eventCreator.CreateAndAnnounceAsync(newEvent, request.ChannelId, Actor);
        if (!created.Success)
        {
            return ProblemFor(created.Error!);
        }

        var eventId = created.Value!.EventId;

        return CreatedAtAction(nameof(GetEventById), new { eventId }, new EventCreatedDto { EventId = eventId });
    }

    [HttpPost("{eventId}/pings/preview")]
    public async Task<ActionResult<PingPreviewDto>> PreviewPing(ulong eventId, [FromBody] PingRequestDto request)
    {
        var (evt, ping, problem) = await LoadForPingAsync(eventId, request);
        if (evt == null)
        {
            return problem!;
        }

        var preview = await _pings.PreviewAsync(evt, ping!);
        if (!preview.Success)
        {
            return ProblemFor(preview.Error!);
        }

        var names = await _directory.GetMemberNamesAsync(evt.GuildId, preview.Value!.Recipients.Select(r => r.UserId).ToList(), HttpContext.RequestAborted);

        return Ok(new PingPreviewDto
        {
            Recipients = preview.Value.Recipients.Select(r => new PingRecipientDto { UserId = r.UserId, DisplayName = names.GetValueOrDefault(r.UserId), Group = r.Group }).ToList(),
            RecentlyPingedAt = preview.Value.RecentlyPingedAt
        });
    }

    [HttpPost("{eventId}/pings")]
    public async Task<ActionResult<PingResultDto>> SendPing(ulong eventId, [FromBody] PingRequestDto request)
    {
        var (evt, ping, problem) = await LoadForPingAsync(eventId, request);
        if (evt == null)
        {
            return problem!;
        }

        var sent = await _pings.SendAsync(evt, ping!, Actor);

        return sent.Success
            ? Ok(new PingResultDto { Count = sent.Value })
            : ProblemFor(sent.Error!);
    }

    private async Task<(Event? Event, PingRequest? Request, ActionResult? Problem)> LoadForPingAsync(ulong eventId, PingRequestDto request)
    {
        var (evt, denied) = await LoadEventAsync(_eventService, eventId);
        if (evt == null)
        {
            return (null, null, denied);
        }

        if (!Enum.TryParse<PingTarget>(request.Target, ignoreCase: true, out var target) || !Enum.IsDefined(target))
        {
            return (null, null, ProblemFor(PingErrors.UnknownTarget));
        }

        return (evt, new PingRequest(target, request.RoleIds, request.IncludeUnsure, request.Note), null);
    }

    [HttpPut("{eventId}")]
    public async Task<ActionResult> UpdateEvent(ulong eventId, [FromBody] UpdateEventRequest request)
    {
        var (evt, denied) = await LoadEventAsync(_eventService, eventId);
        if (evt == null)
        {
            return denied!;
        }

        // A time within the same minute as the current one (the web form drops the seconds) means "not rescheduled".
        // A missing description is left alone; an empty one clears it
        var update = new EventUpdate(
            Title: string.IsNullOrWhiteSpace(request.Title) ? null : request.Title,
            Description: request.Description == evt.Description ? null : request.Description,
            ScheduledTime: IsSameMinute(request.ScheduledTime, evt.ScheduledTime) ? null : request.ScheduledTime,
            DurationMinutes: request.DurationMinutes == (evt.DurationMinutes ?? 0) ? null : request.DurationMinutes,
            ReminderMinutes: request.ReminderMinutes is null || request.ReminderMinutes == (evt.ReminderMinutesBefore ?? 0) ? null : request.ReminderMinutes,
            MentionRoleIds: SameRoles(request.MentionRoleIds, evt.MentionRoleIds) ? null : request.MentionRoleIds);

        var updated = await _eventService.UpdateAsync(evt, update, Actor);
        if (updated.Error == EventErrors.NothingToChange)
        {
            return NoContent();
        }

        return updated.Success ? NoContent() : ProblemFor(updated.Error!);
    }

    [HttpPost("{eventId}/cancel")]
    public async Task<ActionResult> CancelEvent(ulong eventId)
    {
        var (evt, denied) = await LoadEventAsync(_eventService, eventId);
        if (evt == null)
        {
            return denied!;
        }

        // Cancelling one occurrence would stop the series being rolled forward, so a series is only cancelled as a whole
        if (evt.IsRecurring)
        {
            return BadRequest(Problems.Of(StatusCodes.Status400BadRequest, "This is a recurring event. Cancel the whole series instead."));
        }

        var cancelled = await _eventService.CancelAsync(evt, Actor);

        return cancelled.Success ? NoContent() : ProblemFor(cancelled.Error!);
    }

    [HttpDelete("{eventId}")]
    public async Task<ActionResult> DeleteEvent(ulong eventId)
    {
        var (evt, denied) = await LoadEventAsync(_eventService, eventId);
        if (evt == null)
        {
            return denied!;
        }

        var deleted = await _eventService.DeleteAsync(evt, Actor);

        return deleted.Success ? NoContent() : ProblemFor(deleted.Error!);
    }

    [HttpPost("{eventId}/cancelrecurring")]
    public async Task<ActionResult> CancelRecurringEventSeries(ulong eventId)
    {
        var (evt, denied) = await LoadEventAsync(_eventService, eventId);
        if (evt == null)
        {
            return denied!;
        }

        if (!evt.IsRecurring)
        {
            return BadRequest(Problems.Of(StatusCodes.Status400BadRequest, "This is not a recurring event"));
        }

        await _eventService.CancelSeriesAsync(evt, Actor);

        return NoContent();
    }

    private static bool IsSameMinute(DateTime? requested, DateTime current)
    {
        if (!requested.HasValue)
        {
            return true;
        }

        return Math.Abs((requested.Value.ToUniversalTime() - current.ToUniversalTime()).TotalSeconds) < 60;
    }

    private static bool SameRoles(IReadOnlyCollection<ulong>? requested, IReadOnlyCollection<ulong> current)
    {
        return requested == null || requested.ToHashSet().SetEquals(current);
    }
}
