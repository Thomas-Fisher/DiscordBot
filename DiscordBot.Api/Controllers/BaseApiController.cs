using DiscordBot.Application.Errors;
using DiscordBot.Application.Interfaces;
using DiscordBot.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DiscordBot.Application;

namespace DiscordBot.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public abstract class BaseApiController : ControllerBase
{
    protected BaseApiController(IGuildAccessService guildAccess)
    {
        GuildAccess = guildAccess;
    }

    protected IGuildAccessService GuildAccess { get; }

    protected ulong? CurrentUserId =>
        ulong.TryParse(User.FindFirst(AuthClaims.UserId)?.Value, out var userId) ? userId : null;

    /// <summary>
    /// Null when the signed-in user may manage the guild; otherwise the response to send: 403 for someone who may not, or 503 while
    /// the bot is not connected to Discord (nobody's access can be decided then, which is not the same as "no").
    /// </summary>
    protected async Task<ActionResult?> RequireGuildAccessAsync(ulong guildId)
    {
        return await CheckAccessAsync(guildId) switch
        {
            AccessCheck.Allowed => null,
            AccessCheck.Unavailable => NotConnected(),
            _ => Forbid()
        };
    }

    /// <summary>
    /// As <see cref="RequireGuildAccessAsync"/>, for a request addressed by an event id. Someone with no access gets the same 404 as for
    /// an id that does not exist, so event ids (which are guessable) cannot be probed to see which exist.
    /// </summary>
    protected async Task<ActionResult?> RequireEventAccessAsync(ulong guildId)
    {
        return await CheckAccessAsync(guildId) switch
        {
            AccessCheck.Allowed => null,
            AccessCheck.Unavailable => NotConnected(),
            _ => NotFound(Problems.Of(EventErrors.NotFound))
        };
    }

    protected async Task<(Event? Event, ActionResult? Denied)> LoadEventAsync(IEventService events, ulong eventId)
    {
        var evt = await events.FindAsync(eventId, HttpContext.RequestAborted);

        if (evt == null)
        {
            return (null, NotFound(Problems.Of(EventErrors.NotFound)));
        }

        if (await RequireEventAccessAsync(evt.GuildId) is { } denied)
        {
            return (null, denied);
        }

        return (evt, null);
    }

    protected ActionResult ProblemFor(Error error)
    {
        return error.Kind == ErrorKind.NotFound ? NotFound(Problems.Of(error)) : BadRequest(Problems.Of(error));
    }

    private async Task<AccessCheck> CheckAccessAsync(ulong guildId)
    {
        if (!GuildAccess.IsAvailable)
        {
            return AccessCheck.Unavailable;
        }

        return CurrentUserId is { } userId && await GuildAccess.CanManageGuildAsync(userId, guildId)
            ? AccessCheck.Allowed
            : AccessCheck.Denied;
    }

    private ObjectResult NotConnected()
    {
        return StatusCode(StatusCodes.Status503ServiceUnavailable, Problems.Of(StatusCodes.Status503ServiceUnavailable, "The bot is not connected to Discord yet. Try again in a moment."));
    }

    private enum AccessCheck
    {
        Allowed,
        Denied,
        Unavailable
    }
}
