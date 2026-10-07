namespace DiscordBot.Discord.Presentation;

public static class DurationText
{
    /// <summary>Formats a number of minutes the way people say it: "45m", "2h", "2h 30m".</summary>
    public static string Format(int minutes)
    {
        var hours = minutes / 60;
        var remainder = minutes % 60;

        if (hours == 0)
        {
            return $"{remainder}m";
        }

        return remainder == 0 ? $"{hours}h" : $"{hours}h {remainder}m";
    }
}
