namespace DiscordBot.Domain.Common;

/// <summary>
/// A guild's time zone, stored by its IANA name ("Europe/London"). Everything is UTC except where a person types or reads a
/// time, and where a recurring event has to keep its clock time through daylight saving.
/// </summary>
public sealed class GuildTimeZone
{
    // A skipped stretch is walked over a minute at a time until a time that exists (gaps are at most an hour)
    private static readonly TimeSpan GapStep = TimeSpan.FromMinutes(1);

    private readonly TimeZoneInfo _zone;

    private GuildTimeZone(TimeZoneInfo zone, string id)
    {
        _zone = zone;
        Id = id;
    }

    public static GuildTimeZone Utc { get; } = new(TimeZoneInfo.Utc, "UTC");

    public string Id { get; }

    /// <summary>Finds the zone by name (an IANA name, or on Windows a Windows name, which is converted to the IANA one).</summary>
    public static bool TryFind(string? id, out GuildTimeZone zone)
    {
        zone = Utc;

        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        if (string.Equals(id.Trim(), "UTC", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            var found = TimeZoneInfo.FindSystemTimeZoneById(id.Trim());
            var ianaId = IanaIdOf(found);

            if (ianaId == null)
            {
                return false;
            }

            zone = new GuildTimeZone(found, ianaId);
            return true;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return false;
        }
    }

    public static GuildTimeZone FindOrUtc(string? id)
    {
        return TryFind(id, out var zone) ? zone : Utc;
    }

    public static IReadOnlyList<string> AvailableIds()
    {
        return TimeZoneInfo.GetSystemTimeZones()
            .Select(IanaIdOf)
            .OfType<string>()
            .Append("UTC")
            .Distinct()
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The wall-clock time in this zone for a UTC time. The result has no kind: it is a clock reading, not an instant.</summary>
    public DateTime ToLocal(DateTime utc)
    {
        var instant = DateTime.SpecifyKind(utc, DateTimeKind.Utc);

        return DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(instant, _zone), DateTimeKind.Unspecified);
    }

    /// <summary>
    /// The UTC time for a wall-clock time in this zone. A time the clocks skip moves on to the first that exists; a time that
    /// happens twice uses the first.
    /// </summary>
    public DateTime ToUtc(DateTime local)
    {
        var clock = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);

        while (_zone.IsInvalidTime(clock))
        {
            clock = clock.Add(GapStep);
        }

        var offset = _zone.IsAmbiguousTime(clock)
            ? _zone.GetAmbiguousTimeOffsets(clock).Max()
            : _zone.GetUtcOffset(clock);

        return DateTime.SpecifyKind(clock - offset, DateTimeKind.Utc);
    }

    public override string ToString()
    {
        return Id;
    }

    private static string? IanaIdOf(TimeZoneInfo zone)
    {
        if (zone.HasIanaId)
        {
            return zone.Id;
        }

        return TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var ianaId) ? ianaId : null;
    }
}
