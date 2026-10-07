namespace DiscordBot.Domain.Common;

public static class SettingPlaceholder
{
    public const string Prefix = "REPLACE-WITH";

    public static bool IsUnset(string? value)
    {
        return string.IsNullOrWhiteSpace(value) || value.TrimStart().StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);
    }
}
