using Discord;
using Discord.Interactions;
using DiscordBot.Application.Interfaces;
using DiscordBot.Application.Models;
using DiscordBot.Discord.Constants;
using DiscordBot.Discord.Models;
using DiscordBot.Discord.Services;
using DiscordBot.Domain.Common;
using Microsoft.Extensions.Logging;
using DiscordBot.Application.Errors;
using DiscordBot.Domain.Enums;
using DiscordBot.Discord.Presentation;

namespace DiscordBot.Discord.SlashCommands;

/// <summary>
/// Everything after someone starts creating an event: the type they pick, the form they fill in, the optional duration and roles
/// step, and finally creating and announcing the event. Progress between the steps is kept in <see cref="InteractionState"/>.
/// </summary>
public class CreateEventFlow : ModuleBase
{
    private readonly IEventService _events;
    private readonly IEventCreator _creator;
    private readonly ISettingsService _settings;
    private readonly InteractionState _state;
    private readonly ILogger<CreateEventFlow> _logger;

    public CreateEventFlow(IEventService events, IEventCreator creator, ISettingsService settings, InteractionState state, ILogger<CreateEventFlow> logger)
    {
        _events = events;
        _creator = creator;
        _settings = settings;
        _state = state;
        _logger = logger;
    }

    [ComponentInteraction($"{InteractionCustomIds.SelectEventTypePrefix}*")]
    public async Task HandleSelectEventTypeAsync()
    {
        var eventType = EventTypeOptions.Parse(SelectedValue());
        _state.StoreEventType(Context.Guild.Id, Context.User.Id, eventType);

        var modal = await EventFormAsync("Create Event", InteractionCustomIds.CreateEventModal);

        await RespondWithModalAsync(modal.Build());
    }

    [ComponentInteraction($"{InteractionCustomIds.SelectRecurringEventTypePrefix}*")]
    public async Task HandleSelectRecurringEventTypeAsync()
    {
        var eventType = EventTypeOptions.Parse(SelectedValue());
        _state.StoreEventType(Context.Guild.Id, Context.User.Id, eventType);

        var recurrenceMenu = new SelectMenuBuilder()
            .WithCustomId(RecurrencePatternOptions.SelectCustomId)
            .WithPlaceholder("Choose recurrence pattern...")
            .WithMinValues(1)
            .WithMaxValues(1);

        foreach (var option in RecurrencePatternOptions.All)
        {
            recurrenceMenu.AddOption(option.Label, option.Value, option.Description);
        }

        var modal = (await EventFormAsync("Create Recurring Event", InteractionCustomIds.CreateRecurringEventModal))
            .AddLabel("Recurrence Pattern", recurrenceMenu)
            .AddTextInput("Remind non-responders (minutes before)", InteractionCustomIds.ReminderMinutesInput, TextInputStyle.Short, $"optional, up to {EventRules.MaxReminderMinutes}", maxLength: 5, required: false);

        await RespondWithModalAsync(modal.Build());
    }

    /// <summary>The form both flows start with. Discord.Net cannot put select menus in a modal yet, so this is text inputs only.</summary>
    private async Task<ModalBuilder> EventFormAsync(string title, string customId)
    {
        var zone = await _settings.GetTimeZoneAsync(Context.Guild.Id);

        return new ModalBuilder()
            .WithTitle(title)
            .WithCustomId(customId)
            .AddTextInput("Event Title", InteractionCustomIds.EventTitleInput, TextInputStyle.Short, "Annual Gathering", maxLength: EventRules.MaxTitleLength)
            .AddTextInput("Date & Time", InteractionCustomIds.EventDateTimeInput, TextInputStyle.Short, $"{DateTimeFormats.EventInputFormatHint} ({zone.Id})", maxLength: 16)
            .AddTextInput("Description", InteractionCustomIds.EventDescriptionInput, TextInputStyle.Paragraph, "Brief description", maxLength: EventRules.MaxDescriptionLength);
    }

    [ModalInteraction(InteractionCustomIds.CreateEventModal)]
    public async Task HandleCreateEventModalAsync(CreateEventModal modal)
    {
        _logger.LogDebug("Create event form submitted by user {UserId}", Context.User.Id);

        await OfferDetailsStepAsync(modal.EventTitle, modal.EventDescription, modal.EventDateTime, reminderMinutes: null, recurrence: null);
    }

    [ModalInteraction(InteractionCustomIds.CreateRecurringEventModal)]
    public async Task HandleCreateRecurringEventModalAsync(CreateRecurringEventModal modal)
    {
        _logger.LogDebug("Create recurring event form submitted by user {UserId}", Context.User.Id);

        if (ReadRecurrencePatternFromModal() is not { } pattern)
        {
            await RespondAsync("❌ Choose how often the event repeats. Please start again with the command.", ephemeral: true);
            return;
        }

        // Left blank means no reminder
        int? reminderMinutes = null;
        if (!string.IsNullOrWhiteSpace(modal.ReminderMinutes))
        {
            if (!int.TryParse(modal.ReminderMinutes.Trim(), out var minutes))
            {
                await RespondAsync($"❌ {EventErrors.ReminderOutOfRange.Message}", ephemeral: true);
                return;
            }

            reminderMinutes = minutes;
        }

        await OfferDetailsStepAsync(modal.EventTitle, modal.EventDescription, modal.EventDateTime, reminderMinutes, pattern);
    }

    [ComponentInteraction(InteractionCustomIds.CreateEventDetailsButton)]
    public async Task OpenDetailsModalAsync()
    {
        if (_state.GetPendingEvent(Context.Guild.Id, Context.User.Id) == null)
        {
            await RespondAsync(UserMessages.EventCreationExpired, ephemeral: true);
            return;
        }

        var roles = new SelectMenuBuilder()
            .WithCustomId(InteractionCustomIds.EventRolesSelect)
            .WithType(ComponentType.RoleSelect)
            .WithPlaceholder("Choose roles to ping (optional)")
            .WithMinValues(0)
            .WithMaxValues(EventRules.MaxMentionRoles)
            .WithRequired(false);

        var modal = new ModalBuilder()
            .WithTitle("Duration & Roles")
            .WithCustomId(InteractionCustomIds.CreateEventDetailsModal)
            .AddTextInput("Duration in minutes (optional)", InteractionCustomIds.EventDurationInput, TextInputStyle.Short, "e.g. 120", maxLength: 4, required: false)
            .AddLabel("Roles to ping", roles);

        await RespondWithModalAsync(modal.Build());
    }

    [ComponentInteraction(InteractionCustomIds.CreateEventSkipDetailsButton)]
    public async Task CreateWithoutDetailsAsync()
    {
        if (await DeferAndGetPendingAsync() is not { } pending)
        {
            return;
        }

        await CreateFromPendingAsync(pending, durationMinutes: null, Array.Empty<ulong>());
    }

    [ModalInteraction(InteractionCustomIds.CreateEventDetailsModal)]
    public async Task HandleEventDetailsModalAsync(EventDetailsModal modal)
    {
        if (await DeferAndGetPendingAsync() is not { } pending)
        {
            return;
        }

        int? duration = null;
        if (!string.IsNullOrWhiteSpace(modal.Duration))
        {
            if (!int.TryParse(modal.Duration.Trim(), out var minutes))
            {
                await FollowupAsync($"❌ {EventErrors.DurationOutOfRange.Message}", ephemeral: true);
                return;
            }

            duration = minutes;
        }

        await CreateFromPendingAsync(pending, duration, ReadSelectedRoleIds());
    }

    private async Task<PendingEvent?> DeferAndGetPendingAsync()
    {
        await DeferAsync(ephemeral: true);

        var pending = _state.GetPendingEvent(Context.Guild.Id, Context.User.Id);
        if (pending == null)
        {
            await FollowupAsync(UserMessages.EventCreationExpired, ephemeral: true);
        }

        return pending;
    }

    /// <summary>
    /// A modal cannot open a modal, so after the first form a message offers a button for the second (duration and roles) or
    /// one that creates the event as it is.
    /// </summary>
    private async Task OfferDetailsStepAsync(string title, string description, string dateTimeText, int? reminderMinutes, RecurrencePattern? recurrence)
    {
        var zone = await _settings.GetTimeZoneAsync(Context.Guild.Id);
        if (!EventInput.TryParseDateTime(dateTimeText, zone, out var scheduledTime))
        {
            await RespondAsync(EventInput.InvalidFormatMessage(zone), ephemeral: true);
            return;
        }

        if (_state.GetEventType(Context.Guild.Id, Context.User.Id) is not { } eventType)
        {
            // Without this an expired WoW flow would quietly create a generic event
            await RespondAsync(UserMessages.EventCreationExpired, ephemeral: true);
            return;
        }

        var draft = new NewEventRequest(Context.Guild.Id, Context.User.Id, title, description, scheduledTime, eventType, reminderMinutes, recurrence);

        var validation = _events.Validate(draft);
        if (!validation.Success)
        {
            await RespondAsync(EventInput.MessageFor(validation.Error!), ephemeral: true);
            return;
        }

        _state.StorePendingEvent(Context.Guild.Id, Context.User.Id, new PendingEvent(title, description, scheduledTime, eventType, reminderMinutes, recurrence));

        var buttons = new ComponentBuilder()
            .WithButton("Add duration & roles", InteractionCustomIds.CreateEventDetailsButton, ButtonStyle.Primary)
            .WithButton("Create event", InteractionCustomIds.CreateEventSkipDetailsButton, ButtonStyle.Secondary)
            .Build();

        // Read back what was understood, in Discord's own time display, so a wrong zone or a typo shows up before the event exists
        await RespondAsync(
            $"**Almost done.** This starts {DiscordTimestamp.LongDateTime(scheduledTime)} ({DiscordTimestamp.Relative(scheduledTime)}), read as {zone.Id} time.\n" +
            "Add how long it lasts and which roles to ping, or create the event as it is.",
            components: buttons, ephemeral: true);
    }

    private async Task CreateFromPendingAsync(PendingEvent pending, int? durationMinutes, IReadOnlyCollection<ulong> mentionRoleIds)
    {
        var request = new NewEventRequest(
            Context.Guild.Id, Context.User.Id, pending.Title, pending.Description, pending.ScheduledTime, pending.EventType,
            pending.ReminderMinutes, pending.Recurrence, durationMinutes, mentionRoleIds);

        var successText = pending.Recurrence.HasValue ? "Recurring event created!" : "Event created!";
        if (!await CreateAndAnnounceAsync(request, successText))
        {
            return;
        }

        _state.ClearPendingEvent(Context.Guild.Id, Context.User.Id);
        _state.ClearEventType(Context.Guild.Id, Context.User.Id);

        await RemoveStepButtonsAsync();
    }

    private async Task RemoveStepButtonsAsync()
    {
        try
        {
            await ModifyOriginalResponseAsync(message => message.Components = new ComponentBuilder().Build());
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not remove the buttons from the step message");
        }
    }

    private IReadOnlyCollection<ulong> ReadSelectedRoleIds()
    {
        if (Context.Interaction is not IModalInteraction modalInteraction)
        {
            return Array.Empty<ulong>();
        }

        var values = modalInteraction.Data.Components
            .FirstOrDefault(c => c.CustomId == InteractionCustomIds.EventRolesSelect)?
            .Values ?? Array.Empty<string>();

        return values.Select(v => ulong.TryParse(v, out var id) ? id : 0UL).Where(id => id != 0).Distinct().ToList();
    }

    /// <summary>
    /// Creates the event, posts its announcement in the current channel and links the two. Replies to the user either way
    /// and returns whether the event was created.
    /// </summary>
    private async Task<bool> CreateAndAnnounceAsync(NewEventRequest request, string successText)
    {
        var created = await _creator.CreateAndAnnounceAsync(request, Context.Channel.Id, ActivityContext.Discord(Context.User.Id));
        if (!created.Success)
        {
            await FollowupAsync(EventInput.MessageFor(created.Error!), ephemeral: true);
            return false;
        }

        _logger.LogInformation("Event {EventId} created by user {UserId}", created.Value!.EventId, Context.User.Id);
        await FollowupAsync($"✅ {successText} Event ID: `{created.Value.EventId}`", ephemeral: true);
        return true;
    }

    private RecurrencePattern? ReadRecurrencePatternFromModal()
    {
        if (Context.Interaction is not IModalInteraction modalInteraction)
        {
            return null;
        }

        var selected = modalInteraction.Data.Components
            .FirstOrDefault(c => c.CustomId == RecurrencePatternOptions.SelectCustomId)?
            .Values?.FirstOrDefault();

        if (RecurrencePatternOptions.TryParse(selected, out var pattern))
        {
            return pattern;
        }

        _logger.LogWarning("No recurrence pattern found in the modal submission (value: '{Value}')", selected);
        return null;
    }
}
