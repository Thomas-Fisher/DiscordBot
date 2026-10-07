using DiscordBot.Web.Services;
using Microsoft.AspNetCore.Components;

namespace DiscordBot.Web.Components;

public partial class ClassSpecPicker
{
    [Parameter, EditorRequired]
    public string EventKind { get; set; } = "";

    [Parameter]
    public WowIconSet Icons { get; set; } = WowIconSet.None;

    [Parameter]
    public string? WowClass { get; set; }

    [Parameter]
    public string? WowSpec { get; set; }

    [Parameter]
    public EventCallback<(string? WowClass, string? WowSpec)> OnChanged { get; set; }

    [Parameter]
    public string IdPrefix { get; set; } = "";

    private Task PickClassAsync(string className)
    {
        return OnChanged.InvokeAsync(className == WowClass ? (WowClass, WowSpec) : (className, null));
    }

    private Task PickSpecAsync(string spec)
    {
        return OnChanged.InvokeAsync((WowClass, spec));
    }
}
