using DiscordBot.Application.Interfaces;
using DiscordBot.Discord.Services.WoW;
using Microsoft.AspNetCore.Mvc;

namespace DiscordBot.Api.Controllers;

public class WowController : BaseApiController
{
    public WowController(IGuildAccessService guildAccess)
        : base(guildAccess)
    {
    }

    [HttpGet("icons")]
    public ActionResult<IReadOnlyDictionary<string, string>> GetIcons()
    {
        return Ok(WowEmotes.IconUrls());
    }
}
