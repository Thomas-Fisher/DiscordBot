using DiscordBot.Application.Models;
using DiscordBot.Domain.Common;
using DiscordBot.Domain.Enums;

namespace DiscordBot.Application.Interfaces;

public interface ISettingsService
{
    Task<EchoSettings> GetEchoSettingsAsync(ulong guildId);

    /// <summary>Saves whichever of the values are supplied and leaves the others as they were.</summary>
    Task SaveEchoSettingsAsync(ulong guildId, ulong? channelId, bool? echoEnabled);

    /// <summary>The kinds of server activity this guild has switched off. Empty until some are.</summary>
    Task<IReadOnlySet<AuditCategory>> GetDisabledAuditCategoriesAsync(ulong guildId);

    Task SaveDisabledAuditCategoriesAsync(ulong guildId, IEnumerable<AuditCategory> categories);

    /// <summary>The guild's time zone. UTC until one has been chosen.</summary>
    Task<GuildTimeZone> GetTimeZoneAsync(ulong guildId);

    Task SaveTimeZoneAsync(ulong guildId, GuildTimeZone zone);
}
