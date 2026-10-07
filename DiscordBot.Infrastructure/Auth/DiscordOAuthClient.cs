using System.Net.Http.Headers;
using System.Text.Json;
using DiscordBot.Application.Errors;
using DiscordBot.Application.Interfaces;
using DiscordBot.Application.Models;
using DiscordBot.Application.Options;
using DiscordBot.Infrastructure.Auth.Wire;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DiscordBot.Infrastructure.Auth;

public class DiscordOAuthClient : IDiscordOAuthClient
{
    // JsonSerializerOptions caches type metadata, so one shared instance is much cheaper than one per call
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _httpClient;
    private readonly ILogger<DiscordOAuthClient> _logger;
    private readonly DiscordOptions _options;

    public DiscordOAuthClient(HttpClient httpClient, IOptions<DiscordOptions> options, ILogger<DiscordOAuthClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<string?> ExchangeCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = _options.RedirectUri
        });

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsync($"{_options.ApiBaseUrl}/oauth2/token", content, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or Polly.Timeout.TimeoutRejectedException or Polly.CircuitBreaker.BrokenCircuitException)
        {
            throw new DiscordUnavailableException("Discord's token endpoint could not be reached", ex);
        }

        using var _ = response;
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Discord refused the OAuth code: {StatusCode}", response.StatusCode);
            return null;
        }

        var token = await ReadAsync<DiscordTokenResponse>(response, cancellationToken);
        return string.IsNullOrEmpty(token?.AccessToken) ? null : token.AccessToken;
    }

    public async Task<DiscordUser> GetUserAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        using var response = await GetAsync("/users/@me", accessToken, cancellationToken);

        var user = await ReadAsync<DiscordUserResponse>(response, cancellationToken);
        if (user?.Id == null || user.Username == null)
        {
            throw new DiscordUnavailableException("Discord returned no user information");
        }

        return new DiscordUser(user.Id, user.Username, user.Avatar);
    }

    public async Task<IReadOnlyList<DiscordGuildMembership>> GetGuildsAsync(string accessToken, CancellationToken cancellationToken = default)
    {
        using var response = await GetAsync("/users/@me/guilds", accessToken, cancellationToken);

        var guilds = await ReadAsync<List<DiscordGuildResponse>>(response, cancellationToken) ?? new List<DiscordGuildResponse>();

        return guilds
            .Where(g => g.Id != null && g.Name != null)
            .Select(g => new DiscordGuildMembership(g.Id!, g.Name!, g.Icon, g.Owner, g.Permissions))
            .ToList();
    }

    private async Task<HttpResponseMessage> GetAsync(string path, string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _options.ApiBaseUrl + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or Polly.Timeout.TimeoutRejectedException or Polly.CircuitBreaker.BrokenCircuitException)
        {
            throw new DiscordUnavailableException($"Discord {path} could not be reached", ex);
        }

        if (!response.IsSuccessStatusCode)
        {
            var status = response.StatusCode;
            response.Dispose();
            _logger.LogWarning("Discord {Path} failed: {StatusCode}", path, status);
            throw new DiscordUnavailableException($"Discord {path} failed with {(int)status}");
        }

        return response;
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken);

        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }
}
