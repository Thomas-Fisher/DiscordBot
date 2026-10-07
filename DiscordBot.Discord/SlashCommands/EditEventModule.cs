using System.Text;
using Discord;
using Discord.Interactions;
using DiscordBot.Application.Interfaces;
using DiscordBot.Application.Models;
using DiscordBot.Discord.Services;
using Microsoft.Extensions.Logging;
using DiscordBot.Domain.Common;
using DiscordBot.Domain.Entities;
using DiscordBot.Discord.Presentation;

namespace DiscordBot.Discord.SlashCommands;

[RequireContext(ContextType.Guild)]
public class EditEventModule : EventCommandModule
{
    private readonly ISettingsService _settings;

    public EditEventModule(IEventService events, ISettingsService settings, EventPermissionService permissions, ILogger<EditEventModule> logger)
        : base(events, permissions, logger)
    {
        _settings = settings;
    }

    [SlashCommand("editevent", "Edit an existing event details")]
    public Task EditEventAsync(
        [Summary(description: "The unique ID of the event to edit")] ulong eventId,
        [Summary(description: "New event title (optional)")] string? newTitle = null,
        [Summary(description: "New event description (optional)")] string? newDescription = null,
        [Summary(description: "New event date/time (optional, DD-MM-YYYY HH:MM in the server's time zone, see /timezone)")] string? newDateTime = null,
        [Summary(description: "Minutes before the start to ping role members who have not responded (1-10080, 0 turns it off)")] int? reminderMinutes = null,
        [Summary(description: "How long the event lasts, in minutes (1-1440; 0 removes the duration)")] int? durationMinutes = null,
        [Summary(description: "A role to ping (replaces the current roles)")] IRole? role1 = null,
        [Summary(description: "A second role to ping")] IRole? role2 = null,
        [Summary(description: "A third role to ping")] IRole? role3 = null,
        [Summary(description: "Remove all the roles the event pings")] bool clearRoles = false)
    {
        return RunOnEventAsync(eventId, "edit", async evt =>
        {
            DateTime? scheduledTime = null;
            if (!string.IsNullOrWhiteSpace(newDateTime))
            {
                var zone = await _settings.GetTimeZoneAsync(Context.Guild.Id);
                if (!EventInput.TryParseDateTime(newDateTime, zone, out var parsed))
                {
                    return EventInput.InvalidFormatMessage(zone);
                }

                scheduledTime = parsed;
            }

            var pickedRoles = new[] { role1, role2, role3 }.Where(role => role != null).Select(role => role!.Id).Distinct().ToList();
            IReadOnlyCollection<ulong>? mentionRoleIds = clearRoles ? Array.Empty<ulong>() : pickedRoles.Count > 0 ? pickedRoles : null;

            // Blank text options mean "leave unchanged"
            var update = new EventUpdate(
                Title: string.IsNullOrWhiteSpace(newTitle) ? null : newTitle,
                Description: string.IsNullOrWhiteSpace(newDescription) ? null : newDescription,
                ScheduledTime: scheduledTime,
                ReminderMinutes: reminderMinutes,
                DurationMinutes: durationMinutes,
                MentionRoleIds: mentionRoleIds);

            var updated = await Events.UpdateAsync(evt, update, ActivityContext.Discord(Context.User.Id));
            if (!updated.Success)
            {
                return EventInput.MessageFor(updated.Error!);
            }

            Logger.LogInformation("Event {EventId} updated by user {UserId}", eventId, Context.User.Id);
            return Summarise(evt);
        });
    }

    private static string Summarise(Event evt)
    {
        var text = new StringBuilder("✅ Event updated successfully!\n\n");
        text.AppendLine($"**{evt.Title}**");
        text.AppendLine($"📅 {DiscordTimestamp.LongDateTime(evt.ScheduledTime)}");

        if (!string.IsNullOrEmpty(evt.Description))
        {
            text.AppendLine($"📝 {evt.Description}");
        }

        text.Append(evt.ReminderMinutesBefore is { } reminder ? $"⏰ Reminder for people who have not responded: {reminder} minutes before" : "⏰ No reminder");

        if (evt.DurationMinutes is { } duration)
        {
            text.Append($"\n⏱️ Duration: {DurationText.Format(duration)}");
        }

        if (evt.MentionRoleIds.Count > 0)
        {
            text.Append($"\n📣 Pings: {string.Join(" ", evt.MentionRoleIds.Select(id => $"<@&{id}>"))}");
        }

        return text.ToString();
    }
}
