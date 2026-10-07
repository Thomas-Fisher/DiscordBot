using DiscordBot.Application.Errors;
using DiscordBot.Application.Models;
using DiscordBot.Domain.Entities;

namespace DiscordBot.Application.Interfaces;

public interface IEventPingService
{
    Task<Result<PingPreview>> PreviewAsync(Event evt, PingRequest request);

    /// <summary>Pings the people the request describes, as a reply to the announcement. Returns how many were pinged.</summary>
    Task<Result<int>> SendAsync(Event evt, PingRequest request, ActivityContext context);
}
