using DiscordBot.Domain.Common;
using DiscordBot.Domain.Enums;

namespace DiscordBot.Tests.Unit.Domain;

public class RecurrenceCalculatorTests
{
    private static readonly DateTime Wednesday = new(2026, 9, 30, 19, 30, 0, DateTimeKind.Utc);

    private static readonly GuildTimeZone Utc = GuildTimeZone.Utc;
    private static readonly GuildTimeZone London = GuildTimeZone.FindOrUtc("Europe/London");

    private static DateTime At(int year, int month, int day, int hour, int minute) => new(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    [Fact]
    public void Next_MonthlyFromTheEndOfALongMonth_LandsOnTheLastDayOfTheShorterMonth()
    {
        var january31 = At(2026, 1, 31, 20, 0);

        var next = RecurrenceCalculator.Next(january31, RecurrencePattern.Monthly, Utc);

        Assert.Equal(At(2026, 2, 28, 20, 0), next);
    }

    [Theory]
    [InlineData(RecurrencePattern.EveryWednesday, 7)]
    [InlineData(RecurrencePattern.EveryThursday, 1)]
    [InlineData(RecurrencePattern.EveryMonday, 5)]
    [InlineData(RecurrencePattern.EverySunday, 4)]
    public void Next_WeekdayPattern_IsTheNextThatWeekdayAtTheSameTimeOfDay(RecurrencePattern pattern, int expectedDaysAhead)
    {
        var next = RecurrenceCalculator.Next(Wednesday, pattern, Utc);

        Assert.Equal(Wednesday.AddDays(expectedDaysAhead), next);
    }

    [Theory]
    [InlineData(RecurrencePattern.Daily, 1)]
    [InlineData(RecurrencePattern.Weekly, 7)]
    [InlineData(RecurrencePattern.BiWeekly, 14)]
    public void Next_FixedIntervalPattern_AddsThatManyDays(RecurrencePattern pattern, int days)
    {
        Assert.Equal(Wednesday.AddDays(days), RecurrenceCalculator.Next(Wednesday, pattern, Utc));
    }

    [Fact]
    public void NextAfter_SeveralOccurrencesMissed_IsTheFirstOneInTheFuture()
    {
        var after = Wednesday.AddDays(20);

        var next = RecurrenceCalculator.NextAfter(Wednesday, RecurrencePattern.Weekly, after, Utc);

        Assert.Equal(Wednesday.AddDays(21), next);
    }

    [Fact]
    public void Next_MonthlySeriesStartedOnThe31st_ReturnsToThe31stWhenTheMonthIsLongEnough()
    {
        var anchor = At(2026, 1, 31, 20, 0);

        var february = RecurrenceCalculator.Next(anchor, RecurrencePattern.Monthly, Utc, anchor);
        var march = RecurrenceCalculator.Next(february, RecurrencePattern.Monthly, Utc, anchor);
        var april = RecurrenceCalculator.Next(march, RecurrencePattern.Monthly, Utc, anchor);

        Assert.Equal(At(2026, 2, 28, 20, 0), february);
        Assert.Equal(At(2026, 3, 31, 20, 0), march);
        Assert.Equal(At(2026, 4, 30, 20, 0), april);
    }

    [Fact]
    public void Next_MonthlyAcrossTheNewYear_KeepsTheDayAndTime()
    {
        var december = At(2026, 12, 15, 18, 45);

        var next = RecurrenceCalculator.Next(december, RecurrencePattern.Monthly, Utc, december);

        Assert.Equal(At(2027, 1, 15, 18, 45), next);
    }

    [Fact]
    public void Next_PatternWithNoRule_IsAnErrorInsteadOfQuietlyBecomingWeekly()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RecurrenceCalculator.Next(Wednesday, (RecurrencePattern)99, Utc));
    }

    [Fact]
    public void Next_WeeklyEveningEventWhenTheClocksGoBack_StaysAtTheSameLocalTime()
    {
        // 20:00 on a Tuesday in British Summer Time (19:00 UTC
        var beforeChange = At(2026, 10, 20, 19, 0);

        var next = RecurrenceCalculator.Next(beforeChange, RecurrencePattern.Weekly, London, beforeChange);

        // 20:00 in GMT is 20:00 UTC, so the UTC time moved an hour and the local time did not
        Assert.Equal(At(2026, 10, 27, 20, 0), next);
        Assert.Equal(new DateTime(2026, 10, 27, 20, 0, 0), London.ToLocal(next));
    }

    [Fact]
    public void Next_WeeklyEveningEventWhenTheClocksGoForward_StaysAtTheSameLocalTime()
    {
        // 20:00 on a Tuesday in GMT (20:00 UTC
        var beforeChange = At(2027, 3, 23, 20, 0);

        var next = RecurrenceCalculator.Next(beforeChange, RecurrencePattern.Weekly, London, beforeChange);

        Assert.Equal(At(2027, 3, 30, 19, 0), next);
    }

    [Fact]
    public void Next_LocalTimeThatDoesNotExistOnThatDay_MovesOnForThatOccurrenceOnlyAndThenReturns()
    {
        // A weekly 01:30 event; the clocks skip 01:00 to 02:00 on 28 March 2027
        var anchor = At(2027, 3, 21, 1, 30);

        var skipped = RecurrenceCalculator.Next(anchor, RecurrencePattern.Weekly, London, anchor);
        var following = RecurrenceCalculator.Next(skipped, RecurrencePattern.Weekly, London, anchor);

        Assert.Equal(At(2027, 3, 28, 1, 0), skipped);      // 02:00 BST, the first time that day that exists
        Assert.Equal(At(2027, 4, 4, 0, 30), following);    // back to 01:30 (BST is UTC+1)
    }

    [Fact]
    public void Next_SameEventInADifferentZone_FollowsThatZonesClockChanges()
    {
        // 20:00 New York time (EDT, UTC-4) the week before the clocks there go back on 1 Nov 2026
        var newYork = GuildTimeZone.FindOrUtc("America/New_York");
        var beforeChange = At(2026, 10, 27, 0, 0);

        var next = RecurrenceCalculator.Next(beforeChange, RecurrencePattern.Weekly, newYork, beforeChange);

        // 20:00 EDT is 00:00 UTC; the next 20:00 EST is 01:00 UTC
        Assert.Equal(At(2026, 11, 3, 1, 0), next);
    }
}
