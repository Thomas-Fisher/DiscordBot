using DiscordBot.Discord.Services;
using DiscordBot.Domain.Common;

namespace DiscordBot.Tests.Unit.Discord;

public class EventInputTests
{
    private static readonly GuildTimeZone London = GuildTimeZone.FindOrUtc("Europe/London");

    [Fact]
    public void TryParseDateTime_UtcGuild_ReturnsTheSameClockTimeAsUtcWhateverTheHostTimeZone()
    {
        var parsed = EventInput.TryParseDateTime("01-10-2026 20:00", GuildTimeZone.Utc, out var utc);

        Assert.True(parsed);
        Assert.Equal(new DateTime(2026, 10, 1, 20, 0, 0, DateTimeKind.Utc), utc);
        Assert.Equal(DateTimeKind.Utc, utc.Kind);
    }

    [Fact]
    public void TryParseDateTime_GuildInSummerTime_ReadsTheTextAsThatZonesClock()
    {
        EventInput.TryParseDateTime("01-07-2026 20:00", London, out var summer);
        EventInput.TryParseDateTime("01-12-2026 20:00", London, out var winter);

        // 20:00 in London is 19:00 UTC in summer and 20:00 UTC in winter
        Assert.Equal(new DateTime(2026, 7, 1, 19, 0, 0, DateTimeKind.Utc), summer);
        Assert.Equal(new DateTime(2026, 12, 1, 20, 0, 0, DateTimeKind.Utc), winter);
    }

    [Fact]
    public void TryParseDateTime_TimeThatDoesNotExistBecauseTheClocksSkipIt_IsMovedOntoTheFirstTimeThatDoes()
    {
        EventInput.TryParseDateTime("28-03-2027 01:30", London, out var utc);

        Assert.Equal(new DateTime(2027, 3, 28, 1, 0, 0, DateTimeKind.Utc), utc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026-10-01 20:00")]
    [InlineData("32-10-2026 20:00")]
    [InlineData("01-10-2026 25:00")]
    public void TryParseDateTime_InvalidText_ReturnsFalse(string? text)
    {
        var parsed = EventInput.TryParseDateTime(text, London, out _);

        Assert.False(parsed);
    }

    [Fact]
    public void InvalidFormatMessage_NamesTheFormatAndTheZoneAndHowToChangeIt()
    {
        var message = EventInput.InvalidFormatMessage(London);

        Assert.Contains("DD-MM-YYYY HH:MM", message);
        Assert.Contains("Europe/London", message);
        Assert.Contains("/timezone", message);
    }
}
