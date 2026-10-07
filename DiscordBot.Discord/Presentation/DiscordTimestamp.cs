namespace DiscordBot.Discord.Presentation;

/// <summary>Discord's timestamp markup, which each viewer sees in their own timezone and language.</summary>
public static class DiscordTimestamp
{
    public static string LongDate(DateTime utc) => Format(utc, 'D');

    public static string ShortTime(DateTime utc) => Format(utc, 't');

    public static string Relative(DateTime utc) => Format(utc, 'R');

    public static string LongDateTime(DateTime utc) => Format(utc, 'F');

    public static string ShortDateTime(DateTime utc) => Format(utc, 'f');

    private static string Format(DateTime utc, char style)
    {
        var seconds = new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();
        return $"<t:{seconds}:{style}>";
    }
}
