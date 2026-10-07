using DiscordBot.Web.Services;

namespace DiscordBot.Web.Pages;

public partial class Index
{
    protected override async Task OnInitializedAsync()
    {
        var user = await AuthService.GetCurrentUserAsync();
        Navigation.NavigateTo(user != null ? "/guilds" : "/login");
    }
}
