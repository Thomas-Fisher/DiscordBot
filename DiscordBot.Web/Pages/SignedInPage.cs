using DiscordBot.Web.Services;
using Microsoft.AspNetCore.Components;

namespace DiscordBot.Web.Pages;

/// <summary>A page that needs a signed-in user: anyone else is sent to the login page before <see cref="OnSignedInAsync"/> runs.</summary>
public abstract class SignedInPage : ComponentBase
{
    [Inject]
    protected AuthService AuthService { get; set; } = null!;

    [Inject]
    protected NavigationManager Navigation { get; set; } = null!;

    [Inject]
    protected ApiService ApiService { get; set; } = null!;

    protected bool IsAuthenticated { get; private set; }

    protected bool IsLoading { get; set; } = true;

    protected string? ErrorMessage { get; set; }

    protected sealed override async Task OnInitializedAsync()
    {
        if (await AuthService.GetCurrentUserAsync() == null)
        {
            Navigation.NavigateTo("/login");
            return;
        }

        IsAuthenticated = true;
        await OnSignedInAsync();
    }

    /// <summary>Loads the page's data. Only called for a signed-in user.</summary>
    protected virtual Task OnSignedInAsync()
    {
        return Task.CompletedTask;
    }
}
