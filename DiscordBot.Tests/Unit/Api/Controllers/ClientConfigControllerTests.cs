using DiscordBot.Api.Controllers;
using DiscordBot.Application.Options;
using DiscordBot.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace DiscordBot.Tests.Unit.Api.Controllers;

public class ClientConfigControllerTests
{
    [Fact]
    public void GetConfig_ReturnsTheClientIdOnly()
    {
        var controller = new ClientConfigController(Options.Create(new DiscordOptions { ClientId = "app-123", ClientSecret = "secret", BotToken = "token" }));

        var config = Assert.IsType<ClientConfigDto>(Assert.IsType<OkObjectResult>(controller.GetConfig().Result).Value);

        Assert.Equal("app-123", config.DiscordClientId);
    }
}
