using DiscordBot.Contracts;
using Microsoft.AspNetCore.Components;

namespace DiscordBot.Web.Components;

public partial class PingPanel
{
    [Parameter, EditorRequired]
    public ulong EventId { get; set; }

    /// <summary>Roles to choose from when looking for people who have not responded.</summary>
    [Parameter]
    public List<RoleDto> Roles { get; set; } = new();

    /// <summary>The roles pinged when the event was announced; they start out ticked.</summary>
    [Parameter]
    public IReadOnlyCollection<ulong> EventRoleIds { get; set; } = Array.Empty<ulong>();

    /// <summary>Raid groups only exist on WoW events, so only they can ping the roster or standby.</summary>
    [Parameter]
    public bool ShowRoster { get; set; }

    [Parameter]
    public EventCallback<string> OnSent { get; set; }

    private string? _target;
    private readonly HashSet<ulong> _roleIds = new();
    private bool _includeUnsure;
    private string? _note;
    private PingPreviewDto? _preview;
    private string? _problem;
    private string? _sendError;
    private bool _loading;
    private bool _sending;

    private async Task ChooseAsync(string target)
    {
        _target = target;
        _note = null;
        _sendError = null;

        if (target == "NoResponse" && _roleIds.Count == 0)
        {
            foreach (var roleId in EventRoleIds)
            {
                _roleIds.Add(roleId);
            }
        }

        await LoadPreviewAsync();
    }

    private async Task ToggleUnsureAsync(bool include)
    {
        _includeUnsure = include;

        await LoadPreviewAsync();
    }

    private PingRequestDto Request() => new()
    {
        Target = _target!,
        RoleIds = _target == "NoResponse" ? _roleIds.ToList() : null,
        IncludeUnsure = _target == "NoResponse" && _includeUnsure,
        Note = string.IsNullOrWhiteSpace(_note) ? null : _note.Trim()
    };

    private async Task LoadPreviewAsync()
    {
        _loading = true;
        _problem = null;
        _preview = null;

        var result = await ApiService.PreviewPingAsync(EventId, Request());
        _loading = false;

        if (result.IsSuccess)
        {
            _preview = result.Value;
        }
        else
        {
            _problem = result.ErrorMessage;
        }
    }

    private async Task SendAsync()
    {
        _sending = true;
        _sendError = null;

        var result = await ApiService.SendPingAsync(EventId, Request());
        _sending = false;

        if (!result.IsSuccess)
        {
            _sendError = result.ErrorMessage;
            return;
        }

        var count = result.Value!.Count;
        _target = null;
        _preview = null;

        await OnSent.InvokeAsync($"Pinged {count} {(count == 1 ? "person" : "people")} in the event's channel.");
    }

    private void Cancel()
    {
        _target = null;
        _preview = null;
        _problem = null;
        _sendError = null;
    }

    private static int MinutesAgo(DateTime at)
    {
        return Math.Max(0, (int)(DateTime.UtcNow - DateTime.SpecifyKind(at, DateTimeKind.Utc)).TotalMinutes);
    }
}
