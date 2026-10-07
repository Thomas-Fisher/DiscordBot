using Discord.Interactions;
using Discord.WebSocket;

namespace DiscordBot.Discord.SlashCommands;

public class PingModule : ModuleBase
{
    private readonly DiscordSocketClient _client;

    public PingModule(DiscordSocketClient client)
    {
        _client = client;
    }

    [SlashCommand("ping", "Check bot latency")]
    public Task PingAsync()
    {
        return RespondAsync($"Pong! 🏓 Gateway latency: {_client.Latency} ms");
    }
}
