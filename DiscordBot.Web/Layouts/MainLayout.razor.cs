using DiscordBot.Contracts;
using DiscordBot.Web.Services;

namespace DiscordBot.Web.Layouts;

public partial class MainLayout
{
    private bool IsAuthenticated { get; set; }
    private UserInfoDto? CurrentUser { get; set; }
    private List<GuildDto> Guilds { get; set; } = new();
    private GuildDto? SelectedGuild { get; set; }
    private string CurrentPage { get; set; } = "";

    protected override async Task OnInitializedAsync()
    {
        // The layout outlives page navigations, so it follows the session instead of reading it once
        AuthService.Changed += OnAuthChanged;
        Navigation.LocationChanged += OnLocationChanged;

        await LoadUserAsync();
    }

    private async Task LoadUserAsync()
    {
        CurrentUser = await AuthService.GetCurrentUserAsync();
        IsAuthenticated = CurrentUser != null;

        if (IsAuthenticated)
        {
            var guilds = await ApiService.GetGuildsAsync();
            Guilds = guilds.IsSuccess ? guilds.Value! : new();
        }
        else
        {
            Guilds = new();
        }

        DetermineCurrentPage();
    }

    private void OnAuthChanged()
    {
        _ = InvokeAsync(async () =>
        {
            CurrentUser = null;
            await LoadUserAsync();
            StateHasChanged();
        });
    }

    private void OnLocationChanged(object? sender, Microsoft.AspNetCore.Components.Routing.LocationChangedEventArgs e)
    {
        DetermineCurrentPage();
        StateHasChanged();
    }

    void IDisposable.Dispose()
    {
        AuthService.Changed -= OnAuthChanged;
        Navigation.LocationChanged -= OnLocationChanged;
    }

    private void DetermineCurrentPage()
    {
        var uri = Navigation.Uri;

        if (uri.Contains("/guild/"))
        {
            var parts = uri.Split('/');
            var guildIdIndex = Array.IndexOf(parts, "guild") + 1;
            if (guildIdIndex < parts.Length && ulong.TryParse(parts[guildIdIndex], out var guildId))
            {
                SelectedGuild = Guilds.FirstOrDefault(g => g.Id == guildId.ToString());

                if (guildIdIndex + 1 < parts.Length)
                {
                    var section = parts[guildIdIndex + 1];
                    CurrentPage = section switch
                    {
                        var s when s.Contains("events") => "events",
                        var s when s.Contains("settings") => "settings",
                        var s when s.Contains("logs") => "logs",
                        _ => ""
                    };
                }
                else
                {
                    CurrentPage = ""; // Home page
                }
            }
        }
        else
        {
            SelectedGuild = null;
            CurrentPage = "";
        }
    }

    private async Task HandleLogout()
    {
        await AuthService.LogoutAsync();
        Navigation.NavigateTo("/login", forceLoad: true);
    }
}
