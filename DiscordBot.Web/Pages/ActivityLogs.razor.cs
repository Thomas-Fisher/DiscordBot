using DiscordBot.Contracts;
using DiscordBot.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace DiscordBot.Web.Pages;

public partial class ActivityLogs
{
    [Parameter]
    public string GuildId { get; set; } = null!;

    private List<ActivityLogDto> Logs { get; set; } = new();
    private string SelectedActionType { get; set; } = "";

    private const int PageSize = 50;

    // Cursor paging: the cursor that produced the page being shown, the one for the next (older) page, and the
    // cursors of the pages we came from so "Newer" can go back
    private string? CurrentCursor { get; set; }
    private string? NextCursor { get; set; }
    private readonly Stack<string?> _previousCursors = new();

    protected override async Task OnSignedInAsync()
    {
        await Clock.LoadAsync(GuildId);

        var filterFromStorage = await JSRuntime.InvokeAsync<string>("localStorage.getItem", "activity_filter_selected");
        if (!string.IsNullOrEmpty(filterFromStorage))
        {
            SelectedActionType = filterFromStorage;
        }

        await LoadActivityLogs();
    }

    private async Task LoadActivityLogs()
    {
        IsLoading = true;
        ErrorMessage = null;

        var result = await ApiService.GetActivityLogsAsync(GuildId, SelectedActionType, CurrentCursor, PageSize);

        if (result.IsSuccess)
        {
            Logs = result.Value!.Logs;
            NextCursor = result.Value.NextCursor;
        }
        else
        {
            ErrorMessage = result.ErrorMessage;
            Logs = new();
            NextCursor = null;
        }

        IsLoading = false;
    }

    private async Task OnActionTypeChanged(ChangeEventArgs e)
    {
        SelectedActionType = e.Value?.ToString() ?? "";
        await JSRuntime.InvokeVoidAsync("localStorage.setItem", "activity_filter_selected", SelectedActionType);

        // A different filter is a different listing: start again from the newest entries
        CurrentCursor = null;
        _previousCursors.Clear();
        await LoadActivityLogs();
    }

    private async Task NextPage()
    {
        if (NextCursor == null)
        {
            return;
        }

        _previousCursors.Push(CurrentCursor);
        CurrentCursor = NextCursor;
        await LoadActivityLogs();
    }

    private async Task PreviousPage()
    {
        if (_previousCursors.Count == 0)
        {
            return;
        }

        CurrentCursor = _previousCursors.Pop();
        await LoadActivityLogs();
    }

    // "SignedUp" -> "Signed Up"
    private static string FormatActionType(string actionType)
    {
        return System.Text.RegularExpressions.Regex.Replace(actionType, "(?<=[a-z])(?=[A-Z])", " ");
    }
}
