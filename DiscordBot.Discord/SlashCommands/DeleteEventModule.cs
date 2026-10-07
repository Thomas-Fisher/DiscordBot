using Discord.Interactions;
using DiscordBot.Application.Interfaces;
using DiscordBot.Application.Models;
using DiscordBot.Discord.Services;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Discord.SlashCommands;

[RequireContext(ContextType.Guild)]
public class DeleteEventModule : EventCommandModule
{
    public DeleteEventModule(IEventService events, EventPermissionService permissions, ILogger<DeleteEventModule> logger)
        : base(events, permissions, logger)
    {
    }

    [SlashCommand("deleteevent", "Delete an event")]
    public Task DeleteEventAsync([Summary(description: "The unique ID of the event to delete")] ulong eventId)
    {
        return RunOnEventAsync(eventId, "delete", async evt =>
        {
            var deleted = await Events.DeleteAsync(evt, ActivityContext.Discord(Context.User.Id));
            if (!deleted.Success)
            {
                return $"❌ {deleted.Error!.Message}";
            }

            Logger.LogInformation("User {UserId} deleted event {EventId}", Context.User.Id, eventId);
            return $"✅ Event `{eventId}` has been deleted";
        });
    }
}
