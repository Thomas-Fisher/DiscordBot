using DiscordBot.Contracts;
using DiscordBot.Domain.Common;
using DiscordBot.Domain.Wow;
using DiscordBot.Web.Services;
using Microsoft.AspNetCore.Components;

namespace DiscordBot.Web.Components;

public partial class RosterBoard
{
    /// <summary>The event's signups. The board only works with the people who are signed up.</summary>
    [Parameter, EditorRequired]
    public IReadOnlyList<SignupDetailsDto> Signups { get; set; } = Array.Empty<SignupDetailsDto>();

    [Parameter]
    public WowIconSet Icons { get; set; } = WowIconSet.None;

    /// <summary>Called with the roster to save: who is in which group, with the group numbers closed up (1, 2, 3...).</summary>
    [Parameter, EditorRequired]
    public EventCallback<List<RosterAssignment>> OnSave { get; set; }

    [Parameter]
    public bool IsSaving { get; set; }

    private readonly Dictionary<ulong, int> _groups = new();
    private Dictionary<ulong, int> _saved = new();
    private IReadOnlyList<SignupDetailsDto>? _loadedFrom;
    private ulong? _dragging;
    private int? _over;

    private int AutoFillCount { get; set; } = 10;

    /// <summary>People who are signed up, in the order they signed up. They are the ones Auto-fill puts in groups.</summary>
    private IEnumerable<SignupDetailsDto> SignedUp => Signups.Where(s => s.Status == "SignedUp").OrderBy(s => s.SignupOrder ?? int.MaxValue).ThenBy(s => s.SignedUpAt);

    /// <summary>Everyone who can be put in a group: signed up, or unsure.</summary>
    private IEnumerable<SignupDetailsDto> Candidates => Signups.Where(s => s.Status is "SignedUp" or "Unsure").OrderBy(s => s.SignupOrder ?? int.MaxValue).ThenBy(s => s.SignedUpAt);

    private List<SignupDetailsDto> InRoster => Candidates.Where(s => _groups.ContainsKey(s.UserId)).ToList();

    private List<SignupDetailsDto> NotInGroup => SignedUp.Where(s => !_groups.ContainsKey(s.UserId)).ToList();

    private List<SignupDetailsDto> UnsureOut => Signups.Where(s => s.Status == "Unsure" && !_groups.ContainsKey(s.UserId)).OrderBy(s => s.SignedUpAt).ToList();

    private bool IsDirty => !_groups.OrderBy(g => g.Key).SequenceEqual(_saved.OrderBy(g => g.Key));

    /// <summary>The groups that are shown: every group up to the highest one in use, so a person is never in a group that cannot be seen.</summary>
    private IEnumerable<int> ShownGroups => Enumerable.Range(1, _groups.Count == 0 ? 0 : _groups.Values.Max());

    private int NextGroup => (_groups.Count == 0 ? 0 : _groups.Values.Max()) + 1;

    private bool CanAddGroup => NextGroup <= RosterRules.MaxGroups;

    protected override void OnParametersSet()
    {
        // A new list from the server (after a save, or the page loading) replaces whatever was being arranged
        if (!ReferenceEquals(_loadedFrom, Signups))
        {
            _loadedFrom = Signups;
            _groups.Clear();
            foreach (var person in Signups.Where(s => s.Status is "SignedUp" or "Unsure" && s.Group.HasValue))
            {
                _groups[person.UserId] = person.Group!.Value;
            }

            _saved = new Dictionary<ulong, int>(_groups);
            AutoFillCount = Math.Clamp(Math.Max(_groups.Count, 1), 1, RosterRules.GroupSize * RosterRules.MaxGroups);
        }
    }

    private List<SignupDetailsDto> MembersOf(int group)
    {
        return Candidates.Where(s => _groups.TryGetValue(s.UserId, out var g) && g == group).ToList();
    }

    private int? GroupOf(SignupDetailsDto person)
    {
        return _groups.TryGetValue(person.UserId, out var group) ? group : null;
    }

    private bool IsFull(int group)
    {
        return _groups.Values.Count(g => g == group) >= RosterRules.GroupSize;
    }

    private int Count(WowRole role)
    {
        return InRoster.Count(s => WowCatalog.RoleOf(s.WowClass, s.WowSpec) == role);
    }

    private string DropClass(int? group)
    {
        if (_dragging == null || _over != (group ?? 0))
        {
            return "";
        }

        return group is { } target && IsFull(target) && !(_groups.TryGetValue(_dragging.Value, out var current) && current == target) ? "drop-refused" : "drop-ready";
    }

    private static string RoleClass(SignupDetailsDto person)
    {
        return WowCatalog.RoleOf(person.WowClass, person.WowSpec) switch
        {
            WowRole.Tank => "role-tank",
            WowRole.Healer => "role-healer",
            WowRole.Melee or WowRole.Ranged => "role-dps",
            _ => ""
        };
    }

    private static string Name(SignupDetailsDto person)
    {
        return person.DisplayName ?? $"User {person.UserId}";
    }

    private static string Capitalised(string text)
    {
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    private void DropOn(int? group)
    {
        if (_dragging is { } userId)
        {
            Move(userId, group);
        }

        _dragging = null;
        _over = null;
    }

    private void MoveFromSelect(ulong userId, string? value)
    {
        Move(userId, int.TryParse(value, out var group) ? group : null);
    }

    /// <summary>Puts the person in the group (or on standby). A full group, or a group that does not exist, leaves things as they were.</summary>
    private void Move(ulong userId, int? group)
    {
        if (group is not { } target)
        {
            _groups.Remove(userId);
            return;
        }

        if (target < 1 || target > RosterRules.MaxGroups)
        {
            return;
        }

        if (_groups.TryGetValue(userId, out var current) && current == target)
        {
            return;
        }

        if (IsFull(target))
        {
            return;
        }

        _groups[userId] = target;
    }

    /// <summary>The first people to sign up are put into groups of five, in signup order; everyone else goes back on standby.</summary>
    private void AutoFill()
    {
        _groups.Clear();

        var count = Math.Clamp(AutoFillCount, 1, RosterRules.GroupSize * RosterRules.MaxGroups);
        var index = 0;
        foreach (var person in SignedUp.Take(count))
        {
            _groups[person.UserId] = index / RosterRules.GroupSize + 1;
            index++;
        }
    }

    private void Clear()
    {
        _groups.Clear();
    }

    private void Discard()
    {
        _groups.Clear();
        foreach (var pair in _saved)
        {
            _groups[pair.Key] = pair.Value;
        }
    }

    private async Task SaveAsync()
    {
        // Groups emptied along the way would leave gaps (1, 3, 4), so the numbers are closed up before they are saved
        var order = _groups.Values.Distinct().Order().Select((group, index) => (group, number: index + 1)).ToDictionary(x => x.group, x => x.number);
        var assignments = _groups.Select(pair => new RosterAssignment { UserId = pair.Key, Group = order[pair.Value] }).OrderBy(a => a.Group).ThenBy(a => a.UserId).ToList();

        await OnSave.InvokeAsync(assignments);
    }
}
