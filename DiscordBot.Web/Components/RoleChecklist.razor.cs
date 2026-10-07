using DiscordBot.Contracts;
using Microsoft.AspNetCore.Components;

namespace DiscordBot.Web.Components;

public partial class RoleChecklist
{
    [Parameter, EditorRequired]
    public List<RoleDto> Roles { get; set; } = new();

    [Parameter, EditorRequired]
    public HashSet<ulong> Selected { get; set; } = new();

    [Parameter]
    public string? IdPrefix { get; set; }

    [Parameter]
    public EventCallback OnChanged { get; set; }

    private async Task ToggleAsync(ulong roleId, bool selected)
    {
        if (selected)
        {
            Selected.Add(roleId);
        }
        else
        {
            Selected.Remove(roleId);
        }

        await OnChanged.InvokeAsync();
    }
}
