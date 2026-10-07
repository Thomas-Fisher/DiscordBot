using DiscordBot.Application.Options;
using DiscordBot.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DiscordBot.Api.Controllers;

[ApiController]
[Route("api/config")]
[AllowAnonymous]
public class ClientConfigController : ControllerBase
{
    private readonly DiscordOptions _discord;

    public ClientConfigController(IOptions<DiscordOptions> discord)
    {
        _discord = discord.Value;
    }

    [HttpGet]
    public ActionResult<ClientConfigDto> GetConfig()
    {
        return Ok(new ClientConfigDto { DiscordClientId = _discord.ClientId });
    }
}
