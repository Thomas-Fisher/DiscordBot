namespace DiscordBot.Discord.Services.WoW;

public interface IApplicationEmoteStore
{
    Task<IReadOnlyDictionary<string, ulong>> ListAsync();

    Task<ulong> CreateAsync(string name, Stream image);
}
