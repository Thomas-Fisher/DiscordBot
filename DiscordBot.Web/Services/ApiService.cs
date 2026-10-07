using System.Net.Http.Json;
using System.Text.Json;
using DiscordBot.Contracts;

namespace DiscordBot.Web.Services;

/// <summary>
/// The dashboard's calls to the API. Nothing here throws for a failed call: every method returns an <see cref="ApiResult"/> whose
/// message can be shown to the user. The access token and its refresh are handled by <see cref="AccessTokenHandler"/>.
/// </summary>
public class ApiService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, Converters = { new DiscordIdConverter() } };

    private readonly HttpClient _httpClient;

    public ApiService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public Task<ApiResult<List<GuildDto>>> GetGuildsAsync()
    {
        return GetAsync<List<GuildDto>>("/api/guilds");
    }

    public Task<ApiResult<GuildDetailsDto>> GetGuildDetailsAsync(string guildId)
    {
        return GetAsync<GuildDetailsDto>($"/api/guilds/{guildId}");
    }

    public Task<ApiResult<List<RoleDto>>> GetGuildRolesAsync(string guildId)
    {
        return GetAsync<List<RoleDto>>($"/api/guilds/{guildId}/roles");
    }

    public Task<ApiResult<List<ChannelDto>>> GetGuildChannelsAsync(string guildId)
    {
        return GetAsync<List<ChannelDto>>($"/api/guilds/{guildId}/channels");
    }

    public Task<ApiResult<GuildStatisticsDto>> GetGuildStatisticsAsync(string guildId)
    {
        return GetAsync<GuildStatisticsDto>($"/api/guilds/{guildId}/statistics");
    }

    public Task<ApiResult<List<EventDto>>> GetGuildEventsAsync(string guildId)
    {
        return GetAsync<List<EventDto>>($"/api/events/{guildId}");
    }

    public Task<ApiResult<EventDetailDto>> GetEventAsync(ulong eventId)
    {
        return GetAsync<EventDetailDto>($"/api/events/byid/{eventId}");
    }

    /// <summary>Creates the event and announces it in the chosen channel. The event's id is in the result.</summary>
    public Task<ApiResult<EventCreatedDto>> CreateEventAsync(string guildId, CreateEventRequest request)
    {
        return ReadAsync<EventCreatedDto>(() => _httpClient.PostAsJsonAsync($"/api/events/{guildId}", request));
    }

    /// <summary>Saves the event's details. A duration of 0 removes the duration, and a reminder of 0 removes the reminder (null leaves it); the role list replaces the roles that are pinged.</summary>
    public Task<ApiResult> UpdateEventAsync(ulong eventId, string title, string description, DateTime? scheduledTime, int durationMinutes, List<ulong> mentionRoleIds, int? reminderMinutes = null)
    {
        var request = new UpdateEventRequest
        {
            Title = title,
            Description = description,
            ScheduledTime = scheduledTime,
            DurationMinutes = durationMinutes,
            ReminderMinutes = reminderMinutes,
            MentionRoleIds = mentionRoleIds
        };

        return SendAsync(() => _httpClient.PutAsJsonAsync($"/api/events/{eventId}", request));
    }

    public Task<ApiResult> CancelEventAsync(ulong eventId)
    {
        return SendAsync(() => _httpClient.PostAsync($"/api/events/{eventId}/cancel", null));
    }

    public Task<ApiResult> DeleteEventAsync(ulong eventId)
    {
        return SendAsync(() => _httpClient.DeleteAsync($"/api/events/{eventId}"));
    }

    public Task<ApiResult<List<SignupDetailsDto>>> GetEventSignupsAsync(ulong eventId)
    {
        return GetAsync<List<SignupDetailsDto>>($"/api/signups/event/{eventId}");
    }

    public Task<ApiResult<List<MemberDto>>> SearchGuildMembersAsync(string guildId, string? search)
    {
        return GetAsync<List<MemberDto>>($"/api/guilds/{guildId}/members?search={Uri.EscapeDataString(search ?? "")}");
    }

    public Task<ApiResult> CreateSignupAsync(ulong eventId, ulong userId, string? wowClass, string? wowSpec)
    {
        var request = new CreateSignupRequest { UserId = userId, WowClass = wowClass, WowSpec = wowSpec };

        return SendAsync(() => _httpClient.PostAsJsonAsync($"/api/signups/event/{eventId}", request));
    }

    public Task<ApiResult> UpdateSignupAsync(ulong eventId, ulong userId, string? status, string? wowClass, string? wowSpec)
    {
        var request = new UpdateSignupRequest { Status = status, WowClass = wowClass, WowSpec = wowSpec };

        return SendAsync(() => _httpClient.PatchAsJsonAsync($"/api/signups/event/{eventId}/user/{userId}", request));
    }

    public Task<ApiResult<Dictionary<string, string>>> GetWowIconsAsync()
    {
        return GetAsync<Dictionary<string, string>>("/api/wow/icons");
    }

    public Task<ApiResult<PingPreviewDto>> PreviewPingAsync(ulong eventId, PingRequestDto request)
    {
        return ReadAsync<PingPreviewDto>(() => _httpClient.PostAsJsonAsync($"/api/events/{eventId}/pings/preview", request));
    }

    /// <summary>Posts a ping in the event's channel. The number of people pinged is in the result.</summary>
    public Task<ApiResult<PingResultDto>> SendPingAsync(ulong eventId, PingRequestDto request)
    {
        return ReadAsync<PingResultDto>(() => _httpClient.PostAsJsonAsync($"/api/events/{eventId}/pings", request));
    }

    /// <summary>Saves the raid groups of a WoW event. Anyone signed up who is not listed is put on standby.</summary>
    public Task<ApiResult> SetRosterAsync(ulong eventId, List<RosterAssignment> assignments)
    {
        return SendAsync(() => _httpClient.PutAsJsonAsync($"/api/signups/event/{eventId}/roster", new SetRosterRequest { Assignments = assignments }));
    }

    public Task<ApiResult> DeleteSignupAsync(ulong eventId, ulong userId)
    {
        return SendAsync(() => _httpClient.DeleteAsync($"/api/signups/event/{eventId}/user/{userId}"));
    }

    public Task<ApiResult> CancelRecurringEventSeriesAsync(ulong eventId)
    {
        return SendAsync(() => _httpClient.PostAsync($"/api/events/{eventId}/cancelrecurring", null));
    }

    /// <summary>Gets one page of a guild's activity log, newest first. Pass the previous page's <c>NextCursor</c> to continue.</summary>
    public Task<ApiResult<PaginatedActivityLogsDto>> GetActivityLogsAsync(string guildId, string? actionType = null, string? cursor = null, int pageSize = 50)
    {
        var path = string.IsNullOrEmpty(actionType)
            ? $"/api/logs/{guildId}"
            : $"/api/logs/{guildId}/{Uri.EscapeDataString(actionType)}";

        var query = $"?pageSize={pageSize}";
        if (!string.IsNullOrEmpty(cursor))
        {
            query += $"&cursor={Uri.EscapeDataString(cursor)}";
        }

        return GetAsync<PaginatedActivityLogsDto>(path + query);
    }

    public Task<ApiResult<List<string>>> GetTimeZonesAsync()
    {
        return GetAsync<List<string>>("/api/settings/timezones");
    }

    public Task<ApiResult<GuildSettingsDto>> GetGuildSettingsAsync(string guildId)
    {
        return GetAsync<GuildSettingsDto>($"/api/settings/guild/{guildId}");
    }

    /// <summary>Saves the supplied values. A channel of 0 clears it.</summary>
    public Task<ApiResult> UpdateGuildSettingsAsync(string guildId, ulong? echoChannelId, bool? echoEnabled, string? timeZone = null, List<string>? disabledAuditCategories = null)
    {
        var request = new UpdateSettingsRequest { EchoChannelId = echoChannelId, EchoEnabled = echoEnabled, TimeZone = timeZone, DisabledAuditCategories = disabledAuditCategories };

        return SendAsync(() => _httpClient.PostAsJsonAsync($"/api/settings/guild/{guildId}", request));
    }

    private Task<ApiResult<T>> GetAsync<T>(string url)
    {
        return ReadAsync<T>(() => _httpClient.GetAsync(url));
    }

    private async Task<ApiResult<T>> ReadAsync<T>(Func<Task<HttpResponseMessage>> send)
    {
        try
        {
            using var response = await send();

            if (!response.IsSuccessStatusCode)
            {
                return ApiResult<T>.Failure(ApiResult.DescribeFailure(response.StatusCode, await response.Content.ReadAsStringAsync()), response.StatusCode);
            }

            var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions);

            return value == null
                ? ApiResult<T>.Failure("The server sent an empty response.", response.StatusCode)
                : ApiResult<T>.Success(value);
        }
        catch (Exception ex) when (IsCallFailure(ex))
        {
            return ApiResult<T>.Failure(MessageFor(ex));
        }
    }

    private async Task<ApiResult> SendAsync(Func<Task<HttpResponseMessage>> send)
    {
        try
        {
            using var response = await send();

            return response.IsSuccessStatusCode
                ? ApiResult.Success()
                : ApiResult.Failure(ApiResult.DescribeFailure(response.StatusCode, await response.Content.ReadAsStringAsync()), response.StatusCode);
        }
        catch (Exception ex) when (IsCallFailure(ex))
        {
            return ApiResult.Failure(MessageFor(ex));
        }
    }

    // The ways a call can fail without a response from the API: no connection (or a blocked cross-origin call), a timeout, a body that is not what was expected
    private static bool IsCallFailure(Exception ex)
    {
        return ex is HttpRequestException or TaskCanceledException or JsonException;
    }

    private static string MessageFor(Exception ex)
    {
        return ex switch
        {
            JsonException => "The server sent a response the dashboard could not read.",
            TaskCanceledException => "The server took too long to answer. Please try again.",
            _ => "Could not reach the server. Check your connection and try again."
        };
    }
}
