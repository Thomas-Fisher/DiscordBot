using DiscordBot.Contracts;
using Microsoft.AspNetCore.Components;

namespace DiscordBot.Web.Pages;

public partial class GuildHome
{
    [Parameter]
    public string GuildId { get; set; } = null!;

    private GuildDetailsDto? Guild { get; set; }
    private GuildStatisticsDto? Statistics { get; set; }

    protected override async Task OnSignedInAsync()
    {
        var details = await ApiService.GetGuildDetailsAsync(GuildId);

        if (!details.IsSuccess)
        {
            ErrorMessage = details.ErrorMessage;
            IsLoading = false;
            return;
        }

        Guild = details.Value;

        var statistics = await ApiService.GetGuildStatisticsAsync(GuildId);

        if (statistics.IsSuccess)
        {
            Statistics = statistics.Value;
        }
        else
        {
            ErrorMessage = statistics.ErrorMessage;
        }

        IsLoading = false;
    }
}
