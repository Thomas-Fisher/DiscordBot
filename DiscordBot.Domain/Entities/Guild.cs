namespace DiscordBot.Domain.Entities;

public class Guild
{
    public ulong GuildId { get; set; }
    public string? GuildName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<GuildSettings> Settings { get; set; } = new List<GuildSettings>();
    public ICollection<MessageLog> MessageLogs { get; set; } = new List<MessageLog>();
    public ICollection<Event> Events { get; set; } = new List<Event>();
}
