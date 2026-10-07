using DiscordBot.Contracts;
using DiscordBot.Web.Services;

namespace DiscordBot.Web.Pages;

public partial class Guilds
{
    private List<GuildDto> GuildList { get; set; } = new();

    protected override async Task OnSignedInAsync()
    {
        var result = await ApiService.GetGuildsAsync();

        if (result.IsSuccess)
        {
            GuildList = result.Value!;
        }
        else
        {
            ErrorMessage = result.ErrorMessage;
        }

        IsLoading = false;
    }
}
