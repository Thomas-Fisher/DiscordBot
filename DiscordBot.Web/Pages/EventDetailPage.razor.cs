using System.Globalization;
using DiscordBot.Contracts;
using DiscordBot.Web.Components;
using DiscordBot.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace DiscordBot.Web.Pages;

public partial class EventDetailPage
{
    [Parameter]
    public string GuildId { get; set; } = null!;

    [Parameter]
    public string EventId { get; set; } = null!;

    private EventDetailDto? Event { get; set; }
    private List<SignupDetailsDto> Signups { get; set; } = new();
    private string? SuccessMessage { get; set; }
    private string EditTitle { get; set; } = "";
    private string EditDescription { get; set; } = "";
    private DateTime ScheduledTimeLocal { get; set; }
    private DateTime _scheduledTimeAsLoaded;
    private string DurationInput { get; set; } = "";
    private string ReminderInput { get; set; } = "";
    private string _reminderAsLoaded = "";
    private List<RoleDto> Roles { get; set; } = new();
    private HashSet<ulong> SelectedRoleIds { get; set; } = new();
    private WowIconSet Icons { get; set; } = WowIconSet.None;
    private bool _showAddPlayer;
    private ulong? _editingUserId;
    private bool _savingSignup;

    private bool CanEditSignups => Event is { Status: "Scheduled" };

    private IReadOnlySet<ulong> SignedUpIds => Signups.Where(s => s.Status == "SignedUp").Select(s => s.UserId).ToHashSet();

    private static string TypeLabel(string? type)
    {
        return type switch
        {
            "WowClassic" => "WoW Classic",
            "WowForever" => "WoW Forever",
            null => "",
            _ => type
        };
    }

    private static string Capitalised(string text)
    {
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    private static string NameOf(SignupDetailsDto signup)
    {
        return signup.DisplayName ?? $"User {signup.UserId}";
    }

    protected override async Task OnSignedInAsync()
    {
        await LoadEventDetails();
    }

    private async Task LoadEventDetails()
    {
        IsLoading = true;
        ErrorMessage = null;
        SuccessMessage = null;

        if (!ulong.TryParse(EventId, out var eventId))
        {
            ErrorMessage = "Invalid event ID.";
            IsLoading = false;
            return;
        }

        var loaded = await ApiService.GetEventAsync(eventId);
        if (!loaded.IsSuccess)
        {
            ErrorMessage = loaded.ErrorMessage;
            IsLoading = false;
            return;
        }

        Event = loaded.Value;
        await Clock.LoadAsync(Event!.GuildId.ToString());
        EditTitle = Event.Title;
        EditDescription = Event.Description;
        ScheduledTimeLocal = Clock.Zone.ToLocal(Event.ScheduledTime);
        _scheduledTimeAsLoaded = ScheduledTimeLocal;
        DurationInput = Event.DurationMinutes?.ToString() ?? "";
        ReminderInput = _reminderAsLoaded = Event.ReminderMinutes is { } reminder ? (reminder / 60m).ToString("0.##", CultureInfo.InvariantCulture) : "";
        SelectedRoleIds = Event.MentionRoleIds.ToHashSet();

        // The roles and signups only decorate the page, so a failure there is reported without hiding the event
        var roles = await ApiService.GetGuildRolesAsync(Event.GuildId.ToString());
        var signups = await ApiService.GetEventSignupsAsync(eventId);
        Roles = roles.Value ?? new();
        Signups = signups.Value ?? new();
        ErrorMessage = roles.ErrorMessage ?? signups.ErrorMessage;

        if (IsWowEvent && ReferenceEquals(Icons, WowIconSet.None))
        {
            Icons = new WowIconSet((await ApiService.GetWowIconsAsync()).Value);
        }

        IsLoading = false;
    }

    private async Task SaveEvent()
    {
        if (Event == null || !ulong.TryParse(EventId, out var eventId))
        {
            return;
        }

        ErrorMessage = null;
        SuccessMessage = null;

        // An empty box means "no duration", which the API expects as 0
        var duration = 0;
        if (!string.IsNullOrWhiteSpace(DurationInput) && !int.TryParse(DurationInput.Trim(), out duration))
        {
            ErrorMessage = "The duration must be a whole number of minutes.";
            return;
        }

        // The time is only sent when it was changed: converting an untouched time back could move it an hour if it falls in the
        // hour that the clocks repeat
        DateTime? scheduledTime = ScheduledTimeLocal == _scheduledTimeAsLoaded ? null : Clock.Zone.ToUtc(ScheduledTimeLocal);

        // The reminder is only sent when it was changed; a blank box removes it (0)
        int? reminderMinutes = null;
        if (ReminderInput.Trim() != _reminderAsLoaded)
        {
            if (string.IsNullOrWhiteSpace(ReminderInput))
            {
                reminderMinutes = 0;
            }
            else if (decimal.TryParse(ReminderInput.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var hours) && hours > 0)
            {
                reminderMinutes = (int)Math.Round(hours * 60);
            }
            else
            {
                ErrorMessage = "The reminder must be a number of hours, for example 24.";
                return;
            }
        }

        var result = await ApiService.UpdateEventAsync(eventId, EditTitle, EditDescription, scheduledTime, duration, SelectedRoleIds.ToList(), reminderMinutes);

        await ReloadAfterAsync(result, "Event updated successfully.");
    }

    private async Task ReloadAfterAsync(ApiResult result, string successMessage)
    {
        if (!result.IsSuccess)
        {
            ErrorMessage = result.ErrorMessage;
            return;
        }

        await LoadEventDetails();
        SuccessMessage = successMessage;
    }

    private void OnTitleChanged(ChangeEventArgs e)
    {
        EditTitle = e.Value?.ToString() ?? "";
    }

    private void OnDescriptionChanged(ChangeEventArgs e)
    {
        EditDescription = e.Value?.ToString() ?? "";
    }

    private void OnScheduledTimeChanged(ChangeEventArgs e)
    {
        if (DateTime.TryParse(e.Value?.ToString(), out var scheduledTime))
        {
            ScheduledTimeLocal = scheduledTime;
        }
    }

    private bool IsWowEvent => Event?.Type != "Generic";

    private void OnPingSent(string message)
    {
        ErrorMessage = null;
        SuccessMessage = message;
    }

    private bool _savingRoster;

    private async Task SaveRoster(List<RosterAssignment> assignments)
    {
        if (!ulong.TryParse(EventId, out var eventId))
        {
            return;
        }

        ErrorMessage = null;
        SuccessMessage = null;
        _savingRoster = true;

        var result = await ApiService.SetRosterAsync(eventId, assignments);
        _savingRoster = false;

        // Reloading hands the board the saved groups, with their numbers closed up
        await ReloadAfterAsync(result, "Roster saved. The Discord message has been updated.");
    }

    private bool CanCancel => Event is { IsRecurring: false, Status: "Scheduled" };

    private async Task CancelEvent()
    {
        if (!CanCancel || !ulong.TryParse(EventId, out var eventId))
        {
            return;
        }

        if (!await ConfirmAsync("Cancel this event? It stays on record, and its Discord message is updated to show it as cancelled."))
        {
            return;
        }

        ErrorMessage = null;
        var result = await ApiService.CancelEventAsync(eventId);

        await ReloadAfterAsync(result, "Event cancelled.");
    }

    private async Task DeleteEvent()
    {
        if (Event == null || !ulong.TryParse(EventId, out var eventId))
        {
            return;
        }

        if (!await ConfirmAsync("Delete this event?\n\nIt disappears from the dashboard and its announcement is removed from Discord. This cannot be undone from here.\n\nTo keep a record of it, use Cancel Event instead."))
        {
            return;
        }

        ErrorMessage = null;
        var result = await ApiService.DeleteEventAsync(eventId);

        if (result.IsSuccess)
        {
            Navigation.NavigateTo($"/guild/{Event.GuildId}/events");
        }
        else
        {
            ErrorMessage = result.ErrorMessage;
        }
    }

    private void ToggleAddPlayer()
    {
        _showAddPlayer = !_showAddPlayer;
    }

    private async Task OnPlayerAdded(string message)
    {
        _showAddPlayer = false;
        await LoadEventDetails();
        SuccessMessage = message;
    }

    private void ToggleEditSignup(ulong userId)
    {
        _editingUserId = _editingUserId == userId ? null : userId;
    }

    private async Task SaveSignupAsync(ulong userId, SignupEdit edit)
    {
        if (!ulong.TryParse(EventId, out var eventId))
        {
            return;
        }

        ErrorMessage = null;
        SuccessMessage = null;
        _savingSignup = true;
        var result = await ApiService.UpdateSignupAsync(eventId, userId, edit.Status, edit.WowClass, edit.WowSpec);
        _savingSignup = false;

        if (result.IsSuccess)
        {
            _editingUserId = null;
        }

        await ReloadAfterAsync(result, "Signup updated.");
    }

    private async Task RemoveSignup(ulong userId)
    {
        if (!ulong.TryParse(EventId, out var eventId))
        {
            return;
        }

        if (!await ConfirmAsync("Remove this user's signup?"))
        {
            return;
        }

        ErrorMessage = null;
        var result = await ApiService.DeleteSignupAsync(eventId, userId);

        await ReloadAfterAsync(result, "Signup removed.");
    }

    private async Task CancelRecurringSeries()
    {
        if (!ulong.TryParse(EventId, out var eventId))
        {
            return;
        }

        if (!await ConfirmAsync("Cancel the entire recurring series? All future instances will be cancelled."))
        {
            return;
        }

        ErrorMessage = null;
        var result = await ApiService.CancelRecurringEventSeriesAsync(eventId);

        if (result.IsSuccess)
        {
            Navigation.NavigateTo(Event != null ? $"/guild/{Event.GuildId}/events" : "/guilds");
        }
        else
        {
            ErrorMessage = result.ErrorMessage;
        }
    }

    private async Task<bool> ConfirmAsync(string question)
    {
        return await JSRuntime.InvokeAsync<bool>("confirm", question);
    }
}
