using DiscordBot.Domain.Enums;

namespace DiscordBot.Domain.Common;

/// <summary>
/// Works out when a recurring event happens next. The rule is applied to the wall clock of the guild's time zone (so "every Friday
/// at 20:00" stays at 20:00 when the clocks change) and the result is converted back to UTC, which is what is stored.
/// </summary>
public static class RecurrenceCalculator
{
    /// <summary>
    /// The first occurrence strictly after <paramref name="currentUtc"/>. The clock time (and for monthly events the day of the
    /// month) come from <paramref name="anchorUtc"/>, the series' first occurrence: a 31st is the 28th in February and the 31st
    /// again in March.
    /// </summary>
    public static DateTime Next(DateTime currentUtc, RecurrencePattern? pattern, GuildTimeZone zone, DateTime? anchorUtc = null)
    {
        var current = zone.ToLocal(currentUtc);
        var anchor = anchorUtc is { } a ? zone.ToLocal(a) : current;

        var date = pattern switch
        {
            null => current.Date.AddDays(7),
            RecurrencePattern.Daily => current.Date.AddDays(1),
            RecurrencePattern.Weekly => current.Date.AddDays(7),
            RecurrencePattern.BiWeekly => current.Date.AddDays(14),
            RecurrencePattern.Monthly => NextMonth(current.Date, anchor.Day),
            RecurrencePattern.EveryMonday => NextWeekday(current.Date, DayOfWeek.Monday),
            RecurrencePattern.EveryTuesday => NextWeekday(current.Date, DayOfWeek.Tuesday),
            RecurrencePattern.EveryWednesday => NextWeekday(current.Date, DayOfWeek.Wednesday),
            RecurrencePattern.EveryThursday => NextWeekday(current.Date, DayOfWeek.Thursday),
            RecurrencePattern.EveryFriday => NextWeekday(current.Date, DayOfWeek.Friday),
            RecurrencePattern.EverySaturday => NextWeekday(current.Date, DayOfWeek.Saturday),
            RecurrencePattern.EverySunday => NextWeekday(current.Date, DayOfWeek.Sunday),
            _ => throw new ArgumentOutOfRangeException(nameof(pattern), pattern, "No rule for this recurrence pattern")
        };

        return zone.ToUtc(date + anchor.TimeOfDay);
    }

    /// <summary>
    /// Returns the first occurrence after <paramref name="afterUtc"/>, stepping through missed occurrences
    /// (for example when the bot was offline for several periods).
    /// </summary>
    public static DateTime NextAfter(DateTime currentUtc, RecurrencePattern? pattern, DateTime afterUtc, GuildTimeZone zone, DateTime? anchorUtc = null)
    {
        var next = Next(currentUtc, pattern, zone, anchorUtc);

        while (next <= afterUtc)
        {
            next = Next(next, pattern, zone, anchorUtc);
        }

        return next;
    }

    private static DateTime NextMonth(DateTime date, int dayOfMonth)
    {
        var first = date.AddMonths(1);
        var day = Math.Min(dayOfMonth, DateTime.DaysInMonth(first.Year, first.Month));

        return first.AddDays(day - first.Day);
    }

    private static DateTime NextWeekday(DateTime date, DayOfWeek target)
    {
        var daysAhead = ((int)target - (int)date.DayOfWeek + 7) % 7;

        if (daysAhead == 0)
        {
            daysAhead = 7;
        }

        return date.AddDays(daysAhead);
    }
}
