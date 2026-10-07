namespace DiscordBot.Domain.Entities;

public class MessageLog
{
    public ulong MessageId { get; set; }
    public ulong GuildId { get; set; }
    public ulong ChannelId { get; set; }
    public ulong AuthorId { get; set; }
    public string? Content { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LoggedAt { get; set; }
    public bool IsDeleted { get; set; }

    public Guild? Guild { get; set; }
}
