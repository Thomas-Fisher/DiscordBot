using DiscordBot.Application.Models;

namespace DiscordBot.Tests.Unit.Application;

public class ActivityLogCursorTests
{
    [Fact]
    public void TryDecode_AnEncodedCursor_ReturnsTheSameCursorExactly()
    {
        var original = new ActivityLogCursor(new DateTime(2026, 9, 29, 12, 34, 56, 789, DateTimeKind.Utc).AddTicks(1234), 1_790_000_000_123UL);

        var decodedOk = ActivityLogCursor.TryDecode(original.Encode(), out var decoded);

        Assert.True(decodedOk);
        Assert.Equal(original.Timestamp.Ticks, decoded.Timestamp.Ticks);
        Assert.Equal(original.LogId, decoded.LogId);
        Assert.Equal(DateTimeKind.Utc, decoded.Timestamp.Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not base64!!")]
    [InlineData("YWJj")]                                   // "abc": valid base64, wrong shape
    [InlineData("MS4yLjM")]                                // "1.2.3": too many parts
    [InlineData("YWJjLjEyMw")]                             // "abc.123": non-numeric ticks
    [InlineData("LTUuMTIz")]                               // "-5.123": ticks below range
    [InlineData("OTk5OTk5OTk5OTk5OTk5OTk5OTk5LjE")]         // ticks far above DateTime.MaxValue
    public void TryDecode_InvalidInput_ReturnsFalse(string? value)
    {
        var decodedOk = ActivityLogCursor.TryDecode(value, out _);

        Assert.False(decodedOk);
    }
}
