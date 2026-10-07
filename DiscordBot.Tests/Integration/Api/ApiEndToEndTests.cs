using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DiscordBot.Domain.Common;
using DiscordBot.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DiscordBot.Tests.Integration.Api;

/// <summary>Requests through the whole running application, so what the controller tests cannot see is covered: authentication, JSON binding, CORS and error responses.</summary>
public class ApiEndToEndTests : IClassFixture<ApiFixture>
{
    private const ulong UserId = 4242;

    private readonly ApiFixture _api;

    public ApiEndToEndTests(ApiFixture api)
    {
        _api = api;
    }

    private async Task<ulong> SeedEventAsync(ulong guildId, DateTime? scheduledTime = null)
    {
        await using var context = _api.CreateDbContext();

        if (!await context.Guilds.AnyAsync(g => g.GuildId == guildId))
        {
            context.Guilds.Add(new Guild { GuildId = guildId, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        }

        var eventId = UniqueId.Next();
        context.Events.Add(new Event
        {
            EventId = eventId,
            GuildId = guildId,
            CreatedBy = UserId,
            Title = "Raid night",
            Description = "Bring flasks",
            ScheduledTime = scheduledTime ?? DateTime.UtcNow.AddDays(3),
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        return eventId;
    }

    private async Task<Event> StoredEventAsync(ulong eventId)
    {
        await using var context = _api.CreateDbContext();
        return await context.Events.AsNoTracking().SingleAsync(e => e.EventId == eventId);
    }

    [Fact]
    public async Task Get_ClientConfig_WithoutAToken_ReturnsTheDiscordClientIdAndNothingSecret()
    {
        using var client = _api.CreateClient();

        var response = await client.GetAsync("/api/config");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"discordClientId\":\"not-a-real-client-id\"", body);
        Assert.DoesNotContain("not-a-real-secret", body);
        Assert.DoesNotContain("not-a-real-token", body);
    }

    [Fact]
    public async Task Get_EndpointThatNeedsALogin_WithoutAToken_Is401()
    {
        using var client = _api.CreateClient();

        var response = await client.GetAsync("/api/guilds");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_GuildEvents_ForAGuildTheUserDoesNotManage_Is403()
    {
        using var client = _api.CreateClient(UserId);

        var response = await client.GetAsync("/api/events/777001");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_GuildEvents_ForAManagedGuild_ReturnsItsEventsWithTimesLabelledUtc()
    {
        const ulong guildId = 777002;
        _api.ManagedGuilds.Add(guildId);
        await SeedEventAsync(guildId, new DateTime(2099, 10, 1, 20, 0, 0, DateTimeKind.Utc));
        using var client = _api.CreateClient(UserId);

        var response = await client.GetAsync($"/api/events/{guildId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"scheduledTime\":\"2099-10-01T20:00:00Z\"", json);
    }

    [Fact]
    public async Task Put_EventTimeWithoutAnOffset_IsStoredAsThatClockTimeInUtc()
    {
        // What the dashboard's date-time box sends
        const ulong guildId = 777003;
        _api.ManagedGuilds.Add(guildId);
        var eventId = await SeedEventAsync(guildId);
        using var client = _api.CreateClient(UserId);

        var response = await client.PutAsJsonAsync($"/api/events/{eventId}", new { scheduledTime = "2099-10-01T20:00:00" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(new DateTime(2099, 10, 1, 20, 0, 0, DateTimeKind.Utc), (await StoredEventAsync(eventId)).ScheduledTime);
    }

    [Fact]
    public async Task Put_EventTimeWithAnOffset_IsConvertedToUtc()
    {
        const ulong guildId = 777004;
        _api.ManagedGuilds.Add(guildId);
        var eventId = await SeedEventAsync(guildId);
        using var client = _api.CreateClient(UserId);

        var response = await client.PutAsJsonAsync($"/api/events/{eventId}", new { scheduledTime = "2099-10-01T22:00:00+02:00" });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(new DateTime(2099, 10, 1, 20, 0, 0, DateTimeKind.Utc), (await StoredEventAsync(eventId)).ScheduledTime);
    }

    [Fact]
    public async Task Put_EventWithATimeInThePast_IsRejectedWithTheReasonAndNothingChanges()
    {
        const ulong guildId = 777005;
        _api.ManagedGuilds.Add(guildId);
        var eventId = await SeedEventAsync(guildId);
        var before = await StoredEventAsync(eventId);
        using var client = _api.CreateClient(UserId);

        var response = await client.PutAsJsonAsync($"/api/events/{eventId}", new { scheduledTime = "2001-01-01T10:00:00Z" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains("in the future", problem.RootElement.GetProperty("detail").GetString());
        Assert.Equal("event.date_in_past", problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(before.ScheduledTime, (await StoredEventAsync(eventId)).ScheduledTime);
    }

    [Fact]
    public async Task Put_EventWithAnUnreadableTime_IsABadRequestNotAServerError()
    {
        const ulong guildId = 777006;
        _api.ManagedGuilds.Add(guildId);
        var eventId = await SeedEventAsync(guildId);
        using var client = _api.CreateClient(UserId);

        var response = await client.PutAsJsonAsync($"/api/events/{eventId}", new { scheduledTime = "next tuesday" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Options_PreflightFromTheDashboardsOrigin_AllowsItWithCredentials()
    {
        using var client = _api.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/auth/refresh");
        request.Headers.Add("Origin", ApiFixture.AllowedOrigin);
        request.Headers.Add("Access-Control-Request-Method", "POST");

        var response = await client.SendAsync(request);

        Assert.Equal(ApiFixture.AllowedOrigin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.Equal("true", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Credentials")));
    }

    [Fact]
    public async Task Options_PreflightFromAnotherOrigin_IsNotAllowed()
    {
        using var client = _api.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/auth/refresh");
        request.Headers.Add("Origin", "https://evil.example");
        request.Headers.Add("Access-Control-Request-Method", "POST");

        var response = await client.SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Get_LivenessEndpoint_IsHealthyAndAnonymous()
    {
        using var client = _api.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Healthy", body.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Settings_TimeZone_IsUtcUntilChosenThenReadsBackWhatWasSaved()
    {
        const ulong guildId = 777007;
        _api.ManagedGuilds.Add(guildId);
        using var client = _api.CreateClient(UserId);

        var before = await client.GetFromJsonAsync<JsonElement>($"/api/settings/guild/{guildId}");
        var save = await client.PostAsJsonAsync($"/api/settings/guild/{guildId}", new { timeZone = "Europe/London" });
        var after = await client.GetFromJsonAsync<JsonElement>($"/api/settings/guild/{guildId}");

        Assert.Equal("UTC", before.GetProperty("timeZone").GetString());
        Assert.Equal(HttpStatusCode.OK, save.StatusCode);
        Assert.Equal("Europe/London", after.GetProperty("timeZone").GetString());
    }

    [Fact]
    public async Task Settings_UnknownTimeZone_IsRefusedAndTheListOfZonesOffersLondon()
    {
        const ulong guildId = 777008;
        _api.ManagedGuilds.Add(guildId);
        using var client = _api.CreateClient(UserId);

        var refused = await client.PostAsJsonAsync($"/api/settings/guild/{guildId}", new { timeZone = "Mars/Olympus_Mons" });
        var zones = await client.GetFromJsonAsync<List<string>>("/api/settings/timezones");

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Contains("Europe/London", zones!);
    }
}
