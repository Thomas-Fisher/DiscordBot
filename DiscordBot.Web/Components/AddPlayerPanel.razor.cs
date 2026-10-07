using DiscordBot.Contracts;
using DiscordBot.Web.Services;
using Microsoft.AspNetCore.Components;

namespace DiscordBot.Web.Components;

public partial class AddPlayerPanel : IDisposable
{
    private static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(300);

    [Parameter, EditorRequired]
    public string GuildId { get; set; } = "";

    [Parameter, EditorRequired]
    public ulong EventId { get; set; }

    [Parameter, EditorRequired]
    public string EventKind { get; set; } = "";

    [Parameter]
    public WowIconSet Icons { get; set; } = WowIconSet.None;

    [Parameter]
    public IReadOnlySet<ulong> SignedUpIds { get; set; } = new HashSet<ulong>();

    [Parameter]
    public EventCallback<string> OnAdded { get; set; }

    [Parameter]
    public EventCallback OnClose { get; set; }

    private string _search = "";
    private List<MemberDto> _results = new();
    private bool _searching = true;
    private bool _searchFailed;
    private string? _problem;
    private MemberDto? _selected;
    private string? _class;
    private string? _spec;
    private bool _adding;
    private string? _error;
    private CancellationTokenSource? _debounce;

    private bool IsWow => EventKind != "Generic";

    private bool CanAdd => _selected != null && (_class == null) == (_spec == null);

    protected override async Task OnInitializedAsync()
    {
        await SearchAsync();
    }

    private async Task OnSearchInput(ChangeEventArgs e)
    {
        _search = e.Value?.ToString() ?? "";

        _debounce?.Cancel();
        _debounce?.Dispose();
        _debounce = new CancellationTokenSource();

        try
        {
            await Task.Delay(SearchDelay, _debounce.Token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        await SearchAsync();
    }

    private async Task SearchAsync()
    {
        _searching = true;
        var result = await ApiService.SearchGuildMembersAsync(GuildId, _search);

        _searchFailed = !result.IsSuccess;
        _problem = result.ErrorMessage;
        _results = result.Value ?? new();
        _searching = false;
    }

    private void Select(MemberDto member)
    {
        _selected = member;
        _class = null;
        _spec = null;
        _error = null;
    }

    private void OnPicked((string? WowClass, string? WowSpec) picked)
    {
        _class = picked.WowClass;
        _spec = picked.WowSpec;
    }

    private async Task AddAsync()
    {
        if (_selected == null)
        {
            return;
        }

        _error = null;
        _adding = true;
        var result = await ApiService.CreateSignupAsync(EventId, _selected.Id, _class, _spec);
        _adding = false;

        if (!result.IsSuccess)
        {
            _error = result.ErrorMessage;
            return;
        }

        var name = _selected.DisplayName;
        _selected = null;
        _class = null;
        _spec = null;
        await OnAdded.InvokeAsync($"{name} added to the event.");
    }

    public void Dispose()
    {
        _debounce?.Cancel();
        _debounce?.Dispose();
    }
}
