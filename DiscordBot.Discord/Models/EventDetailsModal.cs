using Discord;
using Discord.Interactions;
using DiscordBot.Discord.Constants;

namespace DiscordBot.Discord.Models;

/// <summary>
/// The second step of creating an event. The roles dropdown is part of the modal as sent (see ModalHandler) but is read from
/// the raw submission, like the recurrence dropdown, so only the duration is bound here.
/// </summary>
public class EventDetailsModal : IModal
{
    public string Title => "Duration & Roles";

    [InputLabel("Duration in minutes (optional)")]
    [ModalTextInput(InteractionCustomIds.EventDurationInput, TextInputStyle.Short, "e.g. 120", maxLength: 4)]
    [RequiredInput(false)]
    public string? Duration { get; set; }
}
