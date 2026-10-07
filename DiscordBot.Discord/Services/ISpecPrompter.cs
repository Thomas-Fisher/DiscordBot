using DiscordBot.Domain.Entities;

namespace DiscordBot.Discord.Services;

/// <summary>The Discord side of choosing a WoW spec: asking the member in a private message, and clearing the class reaction they clicked.</summary>
public interface ISpecPrompter
{
    /// <summary>Sends the member the menu to pick a spec for the class. Closed DMs are logged, not thrown.</summary>
    Task SendPickerAsync(Event evt, ulong userId, string className);

    Task RemoveClassReactionAsync(Event evt, ulong userId, string className);
}
