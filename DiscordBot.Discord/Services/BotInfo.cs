using System.Diagnostics;
using System.Reflection;
using Discord.WebSocket;

namespace DiscordBot.Discord.Services;

public static class BotInfo
{
    public static string Version => VersionOf(typeof(BotInfo).Assembly);

    public static string DiscordNetVersion => VersionOf(typeof(DiscordSocketClient).Assembly);

    public static TimeSpan Uptime
    {
        get
        {
            using var process = Process.GetCurrentProcess();

            return DateTime.UtcNow - process.StartTime.ToUniversalTime();
        }
    }

    public static string FormatUptime(TimeSpan uptime)
    {
        return $"{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m";
    }

    public static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double length = bytes;
        var order = 0;

        while (length >= 1024 && order < sizes.Length - 1)
        {
            order++;
            length /= 1024;
        }

        return $"{length:0.##} {sizes[order]}";
    }

    // The informational version can carry build metadata after a "+", which is noise here
    private static string VersionOf(Assembly assembly)
    {
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? assembly.GetName().Version?.ToString() ?? "unknown";

        return version.Split('+')[0];
    }
}
