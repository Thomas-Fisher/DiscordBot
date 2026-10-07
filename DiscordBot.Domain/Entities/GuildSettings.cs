namespace DiscordBot.Domain.Entities;

public class GuildSettings
{
    public int SettingId { get; set; }
    public ulong GuildId { get; set; }
    public string SettingKey { get; set; } = string.Empty;
    public string SettingValue { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }

    public Guild? Guild { get; set; }
}
