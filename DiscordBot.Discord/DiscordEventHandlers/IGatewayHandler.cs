using Discord.WebSocket;

namespace DiscordBot.Discord.DiscordEventHandlers;

/// <summary>
/// Something that listens to Discord gateway events. Register an implementation and it is attached to the client when the
/// bot starts (before it connects), so adding a listener never means editing <c>DiscordClientService</c>.
/// </summary>
public interface IGatewayHandler
{
    void Attach(DiscordSocketClient client);
}
