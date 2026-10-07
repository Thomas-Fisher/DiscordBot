using DiscordBot.Application.Interfaces;
using DiscordBot.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace DiscordBot.Api.Controllers;

public class GuildsController : BaseApiController
{
    public const int MaxMemberResults = 25;

    private readonly IGuildDirectory _directory;
    private readonly IEventService _events;
    private readonly ILogger<GuildsController> _logger;

    public GuildsController(IGuildAccessService guildAccess, IGuildDirectory directory, IEventService events, ILogger<GuildsController> logger)
        : base(guildAccess)
    {
        _directory = directory;
        _events = events;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<List<GuildDto>>> GetUserGuilds()
    {
        if (CurrentUserId is not { } userId)
        {
            return Unauthorized(Problems.Of(StatusCodes.Status401Unauthorized, "User ID not found in token"));
        }

        var guilds = new List<GuildDto>();

        foreach (var guildId in await GuildAccess.GetManageableGuildIdsAsync(userId))
        {
            if (await _directory.FindAsync(guildId, HttpContext.RequestAborted) is { } guild)
            {
                guilds.Add(new GuildDto
                {
                    Id = guild.Id.ToString(),
                    Name = guild.Name,
                    Icon = guild.IconId,
                    IsAdmin = true
                });
            }
        }

        _logger.LogInformation("Retrieved {GuildCount} guilds for user {UserId}", guilds.Count, userId);
        return Ok(guilds);
    }

    [HttpGet("{guildId}")]
    public async Task<ActionResult<GuildDetailsDto>> GetGuildDetails(ulong guildId)
    {
        if (await RequireGuildAccessAsync(guildId) is { } denied)
        {
            return denied;
        }

        if (await _directory.FindAsync(guildId, HttpContext.RequestAborted) is not { } guild)
        {
            return GuildNotFound();
        }

        var ownerNames = await _directory.GetMemberNamesAsync(guildId, [guild.OwnerId], HttpContext.RequestAborted);

        return Ok(new GuildDetailsDto
        {
            Id = guild.Id.ToString(),
            Name = guild.Name,
            Icon = guild.IconId,
            Description = guild.Description,
            MemberCount = guild.MemberCount,
            CreatedAt = guild.CreatedAt,
            OwnerId = guild.OwnerId.ToString(),
            OwnerName = ownerNames.GetValueOrDefault(guild.OwnerId)
        });
    }

    [HttpGet("{guildId}/channels")]
    public async Task<ActionResult<List<ChannelDto>>> GetGuildChannels(ulong guildId)
    {
        if (await RequireGuildAccessAsync(guildId) is { } denied)
        {
            return denied;
        }

        if (await _directory.GetTextChannelsAsync(guildId, HttpContext.RequestAborted) is not { } channels)
        {
            return GuildNotFound();
        }

        return Ok(channels.Select(c => new ChannelDto { Id = c.Id.ToString(), Name = c.Name, Type = ChannelDto.TextChannelType, Position = c.Position }).ToList());
    }

    [HttpGet("{guildId}/roles")]
    public async Task<ActionResult<List<RoleDto>>> GetGuildRoles(ulong guildId)
    {
        if (await RequireGuildAccessAsync(guildId) is { } denied)
        {
            return denied;
        }

        if (await _directory.GetPingableRolesAsync(guildId, HttpContext.RequestAborted) is not { } roles)
        {
            return GuildNotFound();
        }

        return Ok(roles.Select(r => new RoleDto { Id = r.Id.ToString(), Name = r.Name, Position = r.Position }).ToList());
    }

    [HttpGet("{guildId}/members")]
    public async Task<ActionResult<List<MemberDto>>> SearchGuildMembers(ulong guildId, [FromQuery] string? search = null)
    {
        if (await RequireGuildAccessAsync(guildId) is { } denied)
        {
            return denied;
        }

        if (await _directory.SearchMembersAsync(guildId, search, MaxMemberResults, HttpContext.RequestAborted) is not { } members)
        {
            return GuildNotFound();
        }

        return Ok(members.Select(m => new MemberDto { Id = m.Id, DisplayName = m.DisplayName, Username = m.Username, AvatarUrl = m.AvatarUrl }).ToList());
    }

    [HttpGet("{guildId}/statistics")]
    public async Task<ActionResult<GuildStatisticsDto>> GetGuildStatistics(ulong guildId)
    {
        if (await RequireGuildAccessAsync(guildId) is { } denied)
        {
            return denied;
        }

        if (await _directory.FindAsync(guildId, HttpContext.RequestAborted) is not { } guild)
        {
            return GuildNotFound();
        }

        var eventCounts = await _events.GetCountsAsync(guildId, HttpContext.RequestAborted);

        return Ok(new GuildStatisticsDto
        {
            GuildId = guildId,
            GuildName = guild.Name,
            MemberCount = guild.MemberCount,
            TotalEvents = eventCounts.Total,
            UpcomingEvents = eventCounts.Upcoming,
            RolesCount = guild.RoleCount,
            ChannelsCount = guild.ChannelCount,
            CreatedAt = guild.CreatedAt,
            BoostLevel = guild.BoostLevel
        });
    }

    private NotFoundObjectResult GuildNotFound()
    {
        return NotFound(Problems.Of(StatusCodes.Status404NotFound, "Guild not found"));
    }
}
