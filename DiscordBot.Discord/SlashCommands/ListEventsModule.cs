using Discord;
using Discord.Interactions;
using DiscordBot.Application.Interfaces;
using DiscordBot.Discord.Presentation;

namespace DiscordBot.Discord.SlashCommands;

[RequireContext(ContextType.Guild)]
public class ListEventsModule : ModuleBase
{
    private readonly IEventService _events;

    public ListEventsModule(IEventService events)
    {
        _events = events;
    }

    [SlashCommand("listevents", "List upcoming events")]
    public async Task ListEventsAsync()
    {
        await DeferAsync();

        var events = await _events.GetUpcomingAsync(Context.Guild.Id);

        if (events.Count == 0)
        {
            await FollowupAsync("📭 No scheduled events for this guild.");
            return;
        }

        // Event titles are typed by users: show any mentions in them without notifying anyone
        await FollowupAsync(embed: EventListEmbed.Build(events), allowedMentions: AllowedMentions.None);
    }
}
