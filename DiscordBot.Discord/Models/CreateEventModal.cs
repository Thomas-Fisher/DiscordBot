using Discord.Interactions;
using Discord;
using DiscordBot.Domain.Common;
using DiscordBot.Discord.Constants;

namespace DiscordBot.Discord.Models;

public class CreateEventModal : IModal
{
    public string Title => "Create Event";

    [InputLabel("Event Title")]
    [ModalTextInput(InteractionCustomIds.EventTitleInput, TextInputStyle.Short, maxLength: EventRules.MaxTitleLength)]
    public string EventTitle { get; set; } = null!;

    [InputLabel("Date & Time")]
    [ModalTextInput(InteractionCustomIds.EventDateTimeInput, TextInputStyle.Short, "DD-MM-YYYY HH:MM", maxLength: 16)]
    public string EventDateTime { get; set; } = null!;

    [InputLabel("Description")]
    [ModalTextInput(InteractionCustomIds.EventDescriptionInput, TextInputStyle.Paragraph, maxLength: EventRules.MaxDescriptionLength)]
    public string EventDescription { get; set; } = null!;
}
