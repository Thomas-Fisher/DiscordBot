using DiscordBot.Domain.Common;

namespace DiscordBot.Tests.Unit.Domain;

public class GuildTimeZoneTests
{
    private static readonly GuildTimeZone London = GuildTimeZone.FindOrUtc("Europe/London");

    private static DateTime Clock(int year, int month, int day, int hour, int minute) => new(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);

    private static DateTime Utc(int year, int month, int day, int hour, int minute) => new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("Europe/London")]
    [InlineData("America/New_York")]
    [InlineData("Asia/Kolkata")]
    [InlineData("  Europe/London  ")]
    public void TryFind_KnownName_FindsIt(string id)
    {
        Assert.True(GuildTimeZone.TryFind(id, out var zone));
        Assert.Equal(id.Trim(), zone.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Mars/Olympus_Mons")]
    [InlineData("not a zone")]
    public void TryFind_UnknownOrEmptyName_ReturnsFalseAndUtc(string? id)
    {
        Assert.False(GuildTimeZone.TryFind(id, out var zone));
        Assert.Equal("UTC", zone.Id);
    }

    [Theory]
    [InlineData("UTC")]
    [InlineData("utc")]
    public void TryFind_Utc_IsFoundWhateverTheCase(string id)
    {
        Assert.True(GuildTimeZone.TryFind(id, out var zone));
        Assert.Equal("UTC", zone.Id);
    }

    [Fact]
    public void TryFind_WindowsZoneName_IsStoredUnderTheIanaName()
    {
        // Windows names must resolve on Windows; elsewhere they may or may not (it depends on ICU), so there the test only
        // checks that a name which does resolve is stored under the IANA name
        var found = GuildTimeZone.TryFind("GMT Standard Time", out var zone);

        Assert.True(found || !OperatingSystem.IsWindows(), "a Windows zone name should resolve on Windows");
        Assert.Equal(found ? "Europe/London" : "UTC", zone.Id);
    }

    [Fact]
    public void AvailableIds_ListsIanaNamesOnceEachInOrder()
    {
        var ids = GuildTimeZone.AvailableIds();

        Assert.Contains("Europe/London", ids);
        Assert.Contains("UTC", ids);
        Assert.Equal(ids.Distinct().Count(), ids.Count);
        Assert.Equal(ids.OrderBy(id => id, StringComparer.Ordinal), ids);
        Assert.All(ids, id => Assert.True(GuildTimeZone.TryFind(id, out _), $"{id} is offered but cannot be found"));
    }

    [Fact]
    public void ToUtc_SummerTime_IsOneHourBehindTheClock()
    {
        var utc = London.ToUtc(Clock(2026, 7, 1, 20, 0));

        Assert.Equal(Utc(2026, 7, 1, 19, 0), utc);
        Assert.Equal(DateTimeKind.Utc, utc.Kind);
    }

    [Fact]
    public void ToUtc_ZoneWithAHalfHourOffset_Converts()
    {
        Assert.Equal(Utc(2026, 7, 1, 14, 30), GuildTimeZone.FindOrUtc("Asia/Kolkata").ToUtc(Clock(2026, 7, 1, 20, 0)));
    }

    [Fact]
    public void ToUtc_TimeSkippedWhenTheClocksGoForward_MovesOnToTheFirstTimeThatExists()
    {
        // 01:30 does not exist in London on 28 March 2027: the clock goes from 01:00 straight to 02:00
        Assert.Equal(Utc(2027, 3, 28, 1, 0), London.ToUtc(Clock(2027, 3, 28, 1, 30)));
    }

    [Fact]
    public void ToUtc_TimeSkippedInAZoneWithAHalfHourChange_MovesOnToTheEndOfTheGap()
    {
        // Lord Howe Island: at 02:00 on 4 October 2026 the clocks jump 30 minutes, to 02:30 (UTC+10:30 to UTC+11)
        var lordHowe = GuildTimeZone.FindOrUtc("Australia/Lord_Howe");

        Assert.Equal(Utc(2026, 10, 3, 15, 30), lordHowe.ToUtc(Clock(2026, 10, 4, 2, 10)));
    }

    [Fact]
    public void ToUtc_TimeThatHappensTwiceWhenTheClocksGoBack_UsesTheFirstTime()
    {
        // 01:30 happens twice in London on 25 October 2026 (00:30 UTC in summer time, then 01:30 UTC)
        Assert.Equal(Utc(2026, 10, 25, 0, 30), London.ToUtc(Clock(2026, 10, 25, 1, 30)));
    }

    [Fact]
    public void ToLocal_UtcTime_IsTheClockReadingInThatZoneWithNoKind()
    {
        var summer = London.ToLocal(Utc(2026, 7, 1, 19, 0));
        var winter = London.ToLocal(Utc(2026, 12, 1, 19, 0));

        Assert.Equal(Clock(2026, 7, 1, 20, 0), summer);
        Assert.Equal(Clock(2026, 12, 1, 19, 0), winter);
        Assert.Equal(DateTimeKind.Unspecified, summer.Kind);
    }

    [Theory]
    [InlineData("Europe/London")]
    [InlineData("America/New_York")]
    [InlineData("Asia/Kolkata")]
    [InlineData("Pacific/Auckland")]
    public void ToLocal_ThenToUtc_ReturnsTheSameInstantOutsideTheOddHours(string id)
    {
        var zone = GuildTimeZone.FindOrUtc(id);
        var instant = Utc(2026, 5, 17, 9, 15);

        Assert.Equal(instant, zone.ToUtc(zone.ToLocal(instant)));
    }
}
