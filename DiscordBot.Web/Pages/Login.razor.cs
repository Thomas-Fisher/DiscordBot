using DiscordBot.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace DiscordBot.Web.Pages;

public partial class Login
{
    [SupplyParameterFromQuery]
    public string? Code { get; set; }

    [SupplyParameterFromQuery]
    public string? State { get; set; }

    private string? ErrorMessage { get; set; }
    private bool IsProcessing { get; set; }
    private bool IsCallback { get; set; }

    protected override async Task OnInitializedAsync()
    {
        if (string.IsNullOrEmpty(Code))
        {
            return;
        }

        IsCallback = true;
        IsProcessing = true;

        // The remembered state is single-use, whatever the outcome
        var expectedState = await JS.InvokeAsync<string?>("sessionStorage.getItem", OAuthLogin.StateStorageKey);
        await JS.InvokeVoidAsync("sessionStorage.removeItem", OAuthLogin.StateStorageKey);

        if (!OAuthLogin.StateMatches(expectedState, State))
        {
            ErrorMessage = "The sign-in could not be verified (it may have been started in another tab or window). Please try again.";
            IsCallback = false;
        }
        else if (await AuthService.HandleCallbackAsync(Code))
        {
            Navigation.NavigateTo("/guilds");
        }
        else
        {
            ErrorMessage = "Failed to authenticate with Discord. Please try again.";
            IsCallback = false;
        }

        IsProcessing = false;
    }

    private async Task LoginWithDiscord()
    {
        var clientId = await AuthService.GetDiscordClientIdAsync();
        if (string.IsNullOrWhiteSpace(clientId))
        {
            ErrorMessage = "The sign-in could not be started because the API did not answer. Check that the API is running.";
            return;
        }

        var state = OAuthLogin.NewState();
        await JS.InvokeVoidAsync("sessionStorage.setItem", OAuthLogin.StateStorageKey, state);

        Navigation.NavigateTo(OAuthLogin.AuthorizeUrl(clientId, $"{Navigation.BaseUri}callback", state));
    }
}
