using DiscordBot.Contracts;
using DiscordBot.Web.Services;
using Microsoft.AspNetCore.Components;

namespace DiscordBot.Web.Components;

public sealed record SignupEdit(string? Status, string? WowClass, string? WowSpec);

public partial class SignupEditor
{
    private static readonly (string Value, string Label)[] Statuses =
    [
        ("SignedUp", "Signed up"),
        ("Unsure", "Unsure"),
        ("SignedOff", "Signed off")
    ];

    [Parameter, EditorRequired]
    public SignupDetailsDto Signup { get; set; } = null!;

    [Parameter, EditorRequired]
    public string EventKind { get; set; } = "";

    [Parameter]
    public WowIconSet Icons { get; set; } = WowIconSet.None;

    [Parameter]
    public bool Saving { get; set; }

    [Parameter]
    public EventCallback<SignupEdit> OnSave { get; set; }

    [Parameter]
    public EventCallback OnClose { get; set; }

    private string _status = "";
    private string? _class;
    private string? _spec;
    private ulong _editing;

    protected override void OnParametersSet()
    {
        if (_editing != Signup.UserId)
        {
            _editing = Signup.UserId;
            _status = Signup.Status;
            _class = Signup.WowClass;
            _spec = Signup.WowSpec;
        }
    }

    private bool IsWow => EventKind != "Generic";

    private bool ShowPicker => IsWow && _status == "SignedUp";

    private bool ClassSpecChanged => ShowPicker && _class != null && _spec != null && (_class != Signup.WowClass || _spec != Signup.WowSpec);

    private bool CanSave => (_status != Signup.Status || ClassSpecChanged) && !(ShowPicker && (_class == null) != (_spec == null));

    private void OnPicked((string? WowClass, string? WowSpec) picked)
    {
        _class = picked.WowClass;
        _spec = picked.WowSpec;
    }

    private Task SaveAsync()
    {
        var edit = new SignupEdit(
            _status != Signup.Status ? _status : null,
            ClassSpecChanged ? _class : null,
            ClassSpecChanged ? _spec : null);

        return OnSave.InvokeAsync(edit);
    }
}
