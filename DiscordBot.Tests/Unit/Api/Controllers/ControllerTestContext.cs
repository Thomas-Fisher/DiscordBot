using System.Security.Claims;
using DiscordBot.Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace DiscordBot.Tests.Unit.Api.Controllers;

/// <summary>
/// Builds a controller context for a signed-in user, and the fake <see cref="IGuildAccessService"/> the controller is built with
/// (the API reads guild rights live from Discord, not from the token).
/// </summary>
public static class ControllerTestContext
{
    /// <summary>The access service the controller is built with: it answers for the given user and guilds, and the bot is connected.</summary>
    public static IGuildAccessService GuildAccess(ulong userId, params ulong[] manageableGuildIds)
    {
        var guildAccess = new Mock<IGuildAccessService>();
        guildAccess.SetupGet(x => x.IsAvailable).Returns(true);
        guildAccess.Setup(x => x.CanManageGuildAsync(userId, It.IsAny<ulong>()))
            .ReturnsAsync((ulong _, ulong guildId) => manageableGuildIds.Contains(guildId));
        guildAccess.Setup(x => x.GetManageableGuildIdsAsync(userId))
            .ReturnsAsync(manageableGuildIds);

        return guildAccess.Object;
    }

    public static IGuildAccessService DisconnectedGuildAccess()
    {
        var guildAccess = new Mock<IGuildAccessService>();
        guildAccess.SetupGet(x => x.IsAvailable).Returns(false);
        return guildAccess.Object;
    }

    public static ControllerContext Create(ulong userId, params ulong[] manageableGuildIds)
    {
        var identity = new ClaimsIdentity(new List<Claim>
        {
            new("discord_user_id", userId.ToString()),
            new("discord_username", "TestUser")
        }, "TestAuth");

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity),
            RequestServices = new ServiceCollection().BuildServiceProvider()
        };

        return new ControllerContext { HttpContext = httpContext };
    }
}
