using Discord;
using Discord.WebSocket;

namespace DiscordBot.Discord.Services.WoW;

public class DiscordApplicationEmoteStore : IApplicationEmoteStore
{
    private readonly DiscordSocketClient _client;

    public DiscordApplicationEmoteStore(DiscordSocketClient client)
    {
        _client = client;
    }

    public async Task<IReadOnlyDictionary<string, ulong>> ListAsync()
    {
        var emotes = await _client.GetApplicationEmotesAsync();

        return emotes.GroupBy(emote => emote.Name).ToDictionary(group => group.Key, group => group.First().Id);
    }

    public async Task<ulong> CreateAsync(string name, Stream image)
    {
        var emote = await _client.CreateApplicationEmoteAsync(name, new Image(image));

        return emote.Id;
    }
}
