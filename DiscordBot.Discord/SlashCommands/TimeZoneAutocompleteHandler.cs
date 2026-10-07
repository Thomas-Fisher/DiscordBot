using Discord;
using Discord.Interactions;
using DiscordBot.Domain.Common;

namespace DiscordBot.Discord.SlashCommands;

public class TimeZoneAutocompleteHandler : AutocompleteHandler
{
    private const int MaxSuggestions = 25;

    private static readonly IReadOnlyList<string> Ids = GuildTimeZone.AvailableIds();

    public override Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
    {
        var typed = autocompleteInteraction.Data.Current.Value?.ToString() ?? "";

        var suggestions = Ids
            .Where(id => id.Contains(typed.Trim().Replace(' ', '_'), StringComparison.OrdinalIgnoreCase))
            .Take(MaxSuggestions)
            .Select(id => new AutocompleteResult(id, id));

        return Task.FromResult(AutocompletionResult.FromSuccess(suggestions));
    }
}
