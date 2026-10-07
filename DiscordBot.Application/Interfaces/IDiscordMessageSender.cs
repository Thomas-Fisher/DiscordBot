namespace DiscordBot.Application.Interfaces;

public interface IDiscordMessageSender
{
    Task SendMessageAsync(ulong channelId, string content);
}
