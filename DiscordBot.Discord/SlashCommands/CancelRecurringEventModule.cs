using Discord.Interactions;
using DiscordBot.Application.Interfaces;
using DiscordBot.Application.Models;
using DiscordBot.Discord.Services;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Discord.SlashCommands;

[RequireContext(ContextType.Guild)]
public class CancelRecurringEventModule : EventCommandModule
{
    public CancelRecurringEventModule(IEventService events, EventPermissionService permissions, ILogger<CancelRecurringEventModule> logger)
        : base(events, permissions, logger)
    {
    }

    [SlashCommand("cancelrecurringevent", "Cancel a recurring event series")]
    public Task CancelRecurringEventAsync([Summary(description: "The unique ID of any event in the series")] ulong eventId)
    {
        return RunOnEventAsync(eventId, "cancel the series of", async evt =>
        {
            if (!evt.IsRecurring)
            {
                return "❌ This is not a recurring event. Use `/cancelevent` instead.";
            }

            var cancelledCount = await Events.CancelSeriesAsync(evt, ActivityContext.Discord(Context.User.Id));

            Logger.LogInformation("User {UserId} cancelled recurring event series of {EventId} ({Count} instances)", Context.User.Id, eventId, cancelledCount);
            return $"✅ Cancelled recurring event series ({cancelledCount} instances)";
        });
    }
}
