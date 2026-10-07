using Discord.Interactions;
using DiscordBot.Application.Interfaces;
using DiscordBot.Application.Models;
using DiscordBot.Discord.Services;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Discord.SlashCommands;

[RequireContext(ContextType.Guild)]
public class CancelEventModule : EventCommandModule
{
    public CancelEventModule(IEventService events, EventPermissionService permissions, ILogger<CancelEventModule> logger)
        : base(events, permissions, logger)
    {
    }

    [SlashCommand("cancelevent", "Cancel an event")]
    public Task CancelEventAsync([Summary(description: "The unique ID of the event to cancel")] ulong eventId)
    {
        return RunOnEventAsync(eventId, "cancel", async evt =>
        {
            if (evt.IsRecurring)
            {
                return "❌ This is a recurring event. Use `/cancelrecurringevent` to cancel the series.";
            }

            var cancelled = await Events.CancelAsync(evt, ActivityContext.Discord(Context.User.Id));
            if (!cancelled.Success)
            {
                return $"❌ {cancelled.Error!.Message}";
            }

            Logger.LogInformation("User {UserId} cancelled event {EventId}", Context.User.Id, eventId);
            return $"✅ Event `{eventId}` has been cancelled";
        });
    }
}
