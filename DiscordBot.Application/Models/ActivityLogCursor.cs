using System.Text;

namespace DiscordBot.Application.Models;

/// <summary>
/// Position in the activity log listing (newest first): the timestamp and id of the last row already returned.
/// The next page is "everything older than this", which stays fast on deep pages and doesn't shift when new logs arrive.
/// </summary>
public record ActivityLogCursor(DateTime Timestamp, ulong LogId)
{
    public string Encode()
    {
        var raw = $"{Timestamp.Ticks}.{LogId}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static bool TryDecode(string? value, out ActivityLogCursor cursor)
    {
        cursor = default!;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            var padded = value.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');

            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(padded)).Split('.');
            if (parts.Length != 2
                || !long.TryParse(parts[0], out var ticks)
                || !ulong.TryParse(parts[1], out var logId)
                || ticks < DateTime.MinValue.Ticks
                || ticks > DateTime.MaxValue.Ticks)
            {
                return false;
            }

            cursor = new ActivityLogCursor(new DateTime(ticks, DateTimeKind.Utc), logId);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
