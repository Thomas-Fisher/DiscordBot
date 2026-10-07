using System.Globalization;
using DiscordBot.Domain.Common;

namespace DiscordBot.Web.Services;

/// <summary>
/// The dashboard's view of time for the guild being looked at. The API speaks UTC; people read and enter times in the guild's
/// time zone, and this does the conversion in one place.
/// </summary>
public class GuildClock
{
    private readonly ApiService _api;
    private string? _loadedFor;

    public GuildClock(ApiService api)
    {
        _api = api;
    }

    /// <summary>The guild's time zone (UTC until <see cref="LoadAsync"/> has found it).</summary>
    public GuildTimeZone Zone { get; private set; } = GuildTimeZone.Utc;

    public async Task LoadAsync(string guildId)
    {
        if (_loadedFor == guildId)
        {
            return;
        }

        var settings = await _api.GetGuildSettingsAsync(guildId);

        // A failed load falls back to UTC for now and is tried again next time, rather than remembering the fallback
        Zone = settings.IsSuccess ? GuildTimeZone.FindOrUtc(settings.Value!.TimeZone) : GuildTimeZone.Utc;
        _loadedFor = settings.IsSuccess ? guildId : null;
    }

    /// <summary>Forgets the loaded zone, after the guild's setting was changed.</summary>
    public void Invalidate()
    {
        _loadedFor = null;
    }

    public string Format(DateTime utc, string format = "MMM dd, yyyy HH:mm")
    {
        return Zone.ToLocal(utc).ToString(format, CultureInfo.InvariantCulture);
    }
}
