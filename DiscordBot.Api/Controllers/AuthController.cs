using DiscordBot.Application;
using DiscordBot.Application.Interfaces;
using DiscordBot.Contracts;
using DiscordBot.Application.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DiscordBot.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IDiscordOAuthClient _discord;
    private readonly ITokenService _tokenService;
    private readonly IGuildAccessService _guildAccess;
    private readonly IGuildDirectory _directory;
    private readonly JwtOptions _jwt;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IDiscordOAuthClient discord, ITokenService tokenService, IGuildAccessService guildAccess, IGuildDirectory directory,
        IOptions<JwtOptions> jwt, ILogger<AuthController> logger)
    {
        _jwt = jwt.Value;
        _discord = discord;
        _tokenService = tokenService;
        _guildAccess = guildAccess;
        _directory = directory;
        _logger = logger;
    }

    [HttpPost("discord/callback")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthTokenResponse>> DiscordCallback([FromBody] OAuthCallbackRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return BadRequest(Problems.Of(StatusCodes.Status400BadRequest, "Code is required"));
        }

        var discordToken = await _discord.ExchangeCodeAsync(request.Code, HttpContext.RequestAborted);

        if (discordToken == null)
        {
            _logger.LogWarning("Failed to exchange OAuth code");
            return BadRequest(Problems.Of(StatusCodes.Status400BadRequest, "Failed to authenticate with Discord"));
        }

        var discordUser = await _discord.GetUserAsync(discordToken, HttpContext.RequestAborted);
        var guilds = await _discord.GetGuildsAsync(discordToken, HttpContext.RequestAborted);

        // Our own short-lived access token plus a rotating refresh token; Discord's tokens are not kept
        var tokens = await _tokenService.IssueAsync(discordUser);
        RefreshTokenCookie.Set(Response, tokens.RefreshToken, TimeSpan.FromDays(_jwt.RefreshExpiryDays));

        var manageable = guilds
            .Where(g => GuildAccessRules.CanManage(g.Owner, g.Permissions))
            .Select(g => new GuildDto { Id = g.Id, Name = g.Name, Icon = g.Icon, IsAdmin = true })
            .ToList();
        var response = ToResponse(tokens, manageable);

        _logger.LogInformation("User {UserId} authenticated successfully", discordUser.Id);
        return Ok(response);
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<UserInfoDto>> GetCurrentUser()
    {
        var userId = User.FindFirst(AuthClaims.UserId)?.Value;
        var username = User.FindFirst(AuthClaims.Username)?.Value;

        if (!ulong.TryParse(userId, out var userIdValue) || string.IsNullOrEmpty(username))
        {
            return Unauthorized(Problems.Of(StatusCodes.Status401Unauthorized, "Invalid token claims"));
        }

        var guildIds = await _guildAccess.GetManageableGuildIdsAsync(userIdValue);

        var response = new UserInfoDto
        {
            Id = userId!,
            Username = username,
            Avatar = User.FindFirst(AuthClaims.Avatar)?.Value,
            Guilds = new List<GuildDto>()
        };

        foreach (var guildId in guildIds)
        {
            if (await _directory.FindAsync(guildId, HttpContext.RequestAborted) is { } guild)
            {
                response.Guilds.Add(new GuildDto { Id = guild.Id.ToString(), Name = guild.Name, Icon = guild.IconId, IsAdmin = true });
            }
        }

        return Ok(response);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthTokenResponse>> RefreshToken()
    {
        var refreshToken = Request.Cookies[RefreshTokenCookie.Name];
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return Unauthorized(Problems.Of(StatusCodes.Status401Unauthorized, "No session"));
        }

        var refreshed = await _tokenService.RefreshAsync(refreshToken);
        if (refreshed.Tokens is not { } tokens)
        {
            // A request that merely lost a race with another tab must not delete the cookie that tab has just been given
            if (refreshed.SessionEnded)
            {
                RefreshTokenCookie.Clear(Response);
            }

            return Unauthorized(Problems.Of(StatusCodes.Status401Unauthorized, "Invalid refresh token"));
        }

        RefreshTokenCookie.Set(Response, tokens.RefreshToken, TimeSpan.FromDays(_jwt.RefreshExpiryDays));

        _logger.LogInformation("Token refreshed for user {UserId}", tokens.UserId);
        return Ok(ToResponse(tokens));
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<ActionResult> Logout()
    {
        var refreshToken = Request.Cookies[RefreshTokenCookie.Name];
        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            await _tokenService.RevokeAsync(refreshToken);
        }

        RefreshTokenCookie.Clear(Response);
        return NoContent();
    }

    private static AuthTokenResponse ToResponse(TokenPair tokens, List<GuildDto>? guilds = null)
    {
        return new AuthTokenResponse
        {
            AccessToken = tokens.AccessToken,
            ExpiresIn = tokens.ExpiresInSeconds,
            User = new UserInfoDto
            {
                Id = tokens.UserId,
                Username = tokens.Username,
                Avatar = tokens.Avatar,
                Guilds = guilds ?? new List<GuildDto>()
            }
        };
    }
}
