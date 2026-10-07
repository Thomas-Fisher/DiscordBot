using DiscordBot.Domain.Enums;

namespace DiscordBot.Discord.Constants;

public static class RecurrencePatternOptions
{
    public const string SelectCustomId = "select_recurrence_pattern";

    public static readonly IReadOnlyList<(string Label, string Value, string Description, RecurrencePattern Pattern)> All = new[]
    {
        ("Daily", "daily", "Every day", RecurrencePattern.Daily),
        ("Weekly", "weekly", "Every 7 days", RecurrencePattern.Weekly),
        ("Bi-weekly", "biweekly", "Every 14 days", RecurrencePattern.BiWeekly),
        ("Monthly", "monthly", "Same date next month", RecurrencePattern.Monthly),
        ("Every Monday", "every_monday", "Every Monday", RecurrencePattern.EveryMonday),
        ("Every Tuesday", "every_tuesday", "Every Tuesday", RecurrencePattern.EveryTuesday),
        ("Every Wednesday", "every_wednesday", "Every Wednesday", RecurrencePattern.EveryWednesday),
        ("Every Thursday", "every_thursday", "Every Thursday", RecurrencePattern.EveryThursday),
        ("Every Friday", "every_friday", "Every Friday", RecurrencePattern.EveryFriday),
        ("Every Saturday", "every_saturday", "Every Saturday", RecurrencePattern.EverySaturday),
        ("Every Sunday", "every_sunday", "Every Sunday", RecurrencePattern.EverySunday)
    };

    public static bool TryParse(string? value, out RecurrencePattern pattern)
    {
        foreach (var option in All)
        {
            if (option.Value == value)
            {
                pattern = option.Pattern;
                return true;
            }
        }

        pattern = default;
        return false;
    }
}
