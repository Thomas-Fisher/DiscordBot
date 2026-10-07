using DiscordBot.Contracts;
using DiscordBot.Application.Errors;
using DiscordBot.Application.Interfaces;
using DiscordBot.Application.Models;
using Microsoft.AspNetCore.Mvc;

namespace DiscordBot.Api.Controllers;

public class SignupsController : BaseApiController
{
    private readonly IEventService _eventService;
    private readonly ISignupService _signupService;
    private readonly IGuildDirectory _directory;

    public SignupsController(IGuildAccessService guildAccess, IEventService eventService, ISignupService signupService, IGuildDirectory directory)
        : base(guildAccess)
    {
        _directory = directory;
        _eventService = eventService;
        _signupService = signupService;
    }

    private ActivityContext Actor => ActivityContext.Api(CurrentUserId);

    [HttpGet("event/{eventId}")]
    public async Task<ActionResult<List<SignupDetailsDto>>> GetEventSignups(ulong eventId)
    {
        var (evt, denied) = await LoadEventAsync(_eventService, eventId);
        if (evt == null)
        {
            return denied!;
        }

        var signups = await _signupService.GetSignupsAsync(eventId, HttpContext.RequestAborted);
        var names = await _directory.GetMemberNamesAsync(evt.GuildId, signups.Select(s => s.UserId), HttpContext.RequestAborted);

        var response = signups
            .OrderBy(s => s.SignupOrder ?? 0)
            .ThenBy(s => s.SignedUpAt)
            .Select(s => new SignupDetailsDto
            {
                SignupId = s.SignupId,
                UserId = s.UserId,
                DisplayName = names.GetValueOrDefault(s.UserId),
                Status = s.Status.ToString(),
                WowClass = s.WowClass,
                WowSpec = s.WowSpec,
                SignupOrder = s.SignupOrder,
                Group = s.GroupNumber,
                SignedUpAt = s.SignedUpAt,
                SignedOffAt = s.SignedOffAt
            })
            .ToList();

        return Ok(response);
    }

    [HttpPost("event/{eventId}")]
    public async Task<ActionResult> CreateSignup(ulong eventId, [FromBody] CreateSignupRequest request)
    {
        if (request.UserId == 0)
        {
            return BadRequest(Problems.Of(StatusCodes.Status400BadRequest, "UserId is required"));
        }

        var (evt, denied) = await LoadEventAsync(_eventService, eventId);
        if (evt == null)
        {
            return denied!;
        }

        // An admin can sign someone else up, but only someone who is in the server
        if (!await _directory.IsMemberAsync(evt.GuildId, request.UserId, HttpContext.RequestAborted))
        {
            return ProblemFor(SignupErrors.NotAMember);
        }

        var result = await _signupService.SignUpAsync(evt, request.UserId, request.WowClass, request.WowSpec, Actor);
        if (!result.Success)
        {
            return ProblemFor(result.Error!);
        }

        return Ok(new SignupCreatedDto { UserId = request.UserId, Order = result.Value });
    }

    [HttpPatch("event/{eventId}/user/{userId}")]
    public async Task<ActionResult> UpdateSignup(ulong eventId, ulong userId, [FromBody] UpdateSignupRequest request)
    {
        var (evt, denied) = await LoadEventAsync(_eventService, eventId);
        if (evt == null)
        {
            return denied!;
        }

        var result = await _signupService.UpdateAsync(evt, userId, new SignupUpdate(request.Status, request.WowClass, request.WowSpec), Actor);

        return result.Success
            ? NoContent()
            : ProblemFor(result.Error!);
    }

    [HttpDelete("event/{eventId}/user/{userId}")]
    public async Task<ActionResult> DeleteSignup(ulong eventId, ulong userId)
    {
        var (evt, denied) = await LoadEventAsync(_eventService, eventId);
        if (evt == null)
        {
            return denied!;
        }

        var result = await _signupService.SignOffAsync(evt, userId, Actor);

        return result.Success
            ? NoContent()
            : ProblemFor(result.Error!);
    }

    [HttpPut("event/{eventId}/roster")]
    public async Task<ActionResult> SetRoster(ulong eventId, [FromBody] SetRosterRequest request)
    {
        var (evt, denied) = await LoadEventAsync(_eventService, eventId);
        if (evt == null)
        {
            return denied!;
        }

        if (request.Assignments.GroupBy(a => a.UserId).Any(g => g.Count() > 1))
        {
            return BadRequest(Problems.Of(StatusCodes.Status400BadRequest, "Each person can be in one group only."));
        }

        var result = await _signupService.SetRosterAsync(evt, request.Assignments.ToDictionary(a => a.UserId, a => a.Group), Actor);

        return result.Success
            ? NoContent()
            : ProblemFor(result.Error!);
    }
}
