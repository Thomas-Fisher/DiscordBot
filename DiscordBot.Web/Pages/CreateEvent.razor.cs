using System.Globalization;
using DiscordBot.Contracts;
using DiscordBot.Web.Services;
using Microsoft.AspNetCore.Components;

namespace DiscordBot.Web.Pages;

public partial class CreateEvent
{
    [Parameter]
    public string GuildId { get; set; } = null!;

    private static readonly (string Value, string Label)[] EventTypes =
    [
        ("Generic", "Generic"),
        ("WowClassic", "WoW Classic"),
        ("WowForever", "WoW Forever")
    ];

    private static readonly (string Value, string Label)[] RepeatOptions =
    [
        ("", "Does not repeat"),
        ("Daily", "Daily"),
        ("Weekly", "Weekly"),
        ("BiWeekly", "Every two weeks"),
        ("Monthly", "Monthly"),
        ("EveryMonday", "Every Monday"),
        ("EveryTuesday", "Every Tuesday"),
        ("EveryWednesday", "Every Wednesday"),
        ("EveryThursday", "Every Thursday"),
        ("EveryFriday", "Every Friday"),
        ("EverySaturday", "Every Saturday"),
        ("EverySunday", "Every Sunday")
    ];

    private List<ChannelDto> Channels { get; set; } = new();
    private List<RoleDto> Roles { get; set; } = new();
    private HashSet<ulong> SelectedRoleIds { get; set; } = new();
    private bool IsSaving { get; set; }

    private string Title { get; set; } = "";
    private string Description { get; set; } = "";
    private DateTime ScheduledTimeLocal { get; set; }
    private string EventType { get; set; } = "Generic";
    private string ChannelId { get; set; } = "";
    private string Recurrence { get; set; } = "";
    private string DurationInput { get; set; } = "";
    private string ReminderInput { get; set; } = "";

    protected override async Task OnSignedInAsync()
    {
        await Clock.LoadAsync(GuildId);

        var channels = await ApiService.GetGuildChannelsAsync(GuildId);
        if (channels.IsSuccess)
        {
            Channels = channels.Value!;
        }
        else
        {
            ErrorMessage = channels.ErrorMessage;
        }

        // The roles are optional, so a failure to load them leaves the list empty rather than blocking the form
        var roles = await ApiService.GetGuildRolesAsync(GuildId);
        Roles = roles.Value ?? new();

        // The same time tomorrow, on the hour, in the guild's own time zone
        var tomorrow = Clock.Zone.ToLocal(DateTime.UtcNow.AddDays(1));
        ScheduledTimeLocal = new DateTime(tomorrow.Year, tomorrow.Month, tomorrow.Day, tomorrow.Hour, 0, 0);

        IsLoading = false;
    }

    private async Task CreateAsync()
    {
        ErrorMessage = null;

        if (!ulong.TryParse(ChannelId, out var channelId))
        {
            ErrorMessage = "Choose the channel to announce the event in.";
            return;
        }

        // Left blank means no reminder
        int? reminderMinutes = null;
        if (!string.IsNullOrWhiteSpace(ReminderInput))
        {
            if (!decimal.TryParse(ReminderInput.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var hours) || hours <= 0)
            {
                ErrorMessage = "The reminder must be a number of hours, for example 24.";
                return;
            }

            reminderMinutes = (int)Math.Round(hours * 60);
        }

        int? durationMinutes = null;
        if (!string.IsNullOrWhiteSpace(DurationInput))
        {
            if (!int.TryParse(DurationInput.Trim(), out var minutes))
            {
                ErrorMessage = "The duration must be a whole number of minutes.";
                return;
            }

            durationMinutes = minutes;
        }

        var request = new CreateEventRequest
        {
            Title = Title,
            Description = Description,
            ScheduledTime = Clock.Zone.ToUtc(ScheduledTimeLocal),
            Type = EventType,
            ChannelId = channelId,
            ReminderMinutes = reminderMinutes,
            DurationMinutes = durationMinutes,
            MentionRoleIds = SelectedRoleIds.ToList(),
            Recurrence = string.IsNullOrEmpty(Recurrence) ? null : Recurrence
        };

        IsSaving = true;
        var result = await ApiService.CreateEventAsync(GuildId, request);
        IsSaving = false;

        if (result.IsSuccess)
        {
            Navigation.NavigateTo($"/guild/{GuildId}/event/{result.Value!.EventId}");
        }
        else
        {
            ErrorMessage = result.ErrorMessage;
        }
    }
}
