using DiscordBot.Contracts;
using DiscordBot.Web.Services;
using Microsoft.AspNetCore.Components;

namespace DiscordBot.Web.Pages;

public partial class GuildEvents
{
    [Parameter]
    public string GuildId { get; set; } = null!;

    private List<EventDto> Events { get; set; } = new();
    private DateTime Now { get; set; }
    private List<EventDto> Upcoming { get; set; } = new();
    private List<EventDto> Past { get; set; } = new();

    protected override async Task OnSignedInAsync()
    {
        await Clock.LoadAsync(GuildId);
        var result = await ApiService.GetGuildEventsAsync(GuildId);

        if (result.IsSuccess)
        {
            Events = result.Value!;
            Now = DateTime.UtcNow;

            // What has not finished yet comes first, soonest at the top; what is over follows, most recent first
            Upcoming = Events.Where(IsUpcoming).OrderBy(e => e.ScheduledTime).ToList();
            Past = Events.Where(e => !IsUpcoming(e)).OrderByDescending(e => e.ScheduledTime).ToList();
        }
        else
        {
            ErrorMessage = result.ErrorMessage;
        }

        IsLoading = false;
    }

    private bool IsUpcoming(EventDto evt)
    {
        return evt.Status == "Scheduled" && evt.ScheduledTime.AddMinutes(evt.DurationMinutes ?? 0) > Now;
    }
}
