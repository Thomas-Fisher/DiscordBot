using Discord.Net;
using DiscordBot.Discord;

namespace DiscordBot.Tests.Unit.Discord;

public class DiscordClientServiceTests
{
    [Theory]
    [InlineData(4013)]
    [InlineData(4014)]
    public void IntentsHint_ConnectionClosedForADisallowedIntent_ExplainsWhichPortalSettingsToChange(int closeCode)
    {
        var hint = DiscordClientService.IntentsHint(new WebSocketClosedException(closeCode));

        Assert.Contains("Server Members Intent", hint);
        Assert.Contains("Message Content Intent", hint);
    }

    [Fact]
    public void IntentsHint_ClosedExceptionWrappedInAnother_IsStillRecognised()
    {
        var wrapped = new InvalidOperationException("gateway failed", new WebSocketClosedException(4014));

        Assert.NotNull(DiscordClientService.IntentsHint(wrapped));
    }

    [Theory]
    [InlineData(1000)]
    [InlineData(4004)]   // bad token: a different problem
    public void IntentsHint_AnyOtherClose_IsNull(int closeCode)
    {
        Assert.Null(DiscordClientService.IntentsHint(new WebSocketClosedException(closeCode)));
    }

    [Fact]
    public void IntentsHint_NoException_IsNull()
    {
        Assert.Null(DiscordClientService.IntentsHint(null));
    }
}
