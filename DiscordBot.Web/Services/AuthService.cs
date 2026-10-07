using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DiscordBot.Contracts;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiscordBot.Web.Services;

/// <summary>
/// The dashboard's session: the access token in memory (<see cref="TokenService"/>), the refresh token in an HttpOnly cookie,
/// so a page reload restores the session by calling refresh.
/// </summary>
public class AuthService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, Converters = { new DiscordIdConverter() } };

    private readonly HttpClient _httpClient;
    private readonly TokenService _tokenService;
    private readonly ILogger<AuthService> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private UserInfoDto? _currentUser;
    private Task<UserInfoDto?>? _loadingUser;

    public AuthService(HttpClient httpClient, TokenService tokenService, ILogger<AuthService>? logger = null)
    {
        _httpClient = httpClient;
        _tokenService = tokenService;
        _logger = logger ?? NullLogger<AuthService>.Instance;
    }

    /// <summary>Raised when someone signs in or out, so parts of the page that show the signed-in user can update themselves.</summary>
    public event Action? Changed;

    public async Task<string?> GetDiscordClientIdAsync()
    {
        try
        {
            var config = await _httpClient.GetFromJsonAsync<ClientConfigDto>("/api/config", JsonOptions);

            return config?.DiscordClientId;
        }
        catch (Exception ex) when (IsCallFailure(ex))
        {
            _logger.LogWarning(ex, "Could not read the dashboard settings from the API");
            return null;
        }
    }

    public async Task<bool> HandleCallbackAsync(string code)
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync("/api/auth/discord/callback", new { code });

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Sign-in was rejected by the API ({Status})", (int)response.StatusCode);
                return false;
            }

            var authResponse = await AcceptTokenAsync(response);
            if (authResponse == null)
            {
                return false;
            }

            _currentUser = authResponse.User;
            Changed?.Invoke();

            return true;
        }
        catch (Exception ex) when (IsCallFailure(ex))
        {
            _logger.LogWarning(ex, "Sign-in failed");
            return false;
        }
    }

    public async Task<UserInfoDto?> GetCurrentUserAsync()
    {
        if (_currentUser != null)
        {
            return _currentUser;
        }

        // Several components ask at once when a page loads; they share one restore instead of each calling the API
        _loadingUser ??= LoadCurrentUserAsync();
        try
        {
            return await _loadingUser;
        }
        finally
        {
            _loadingUser = null;
        }
    }

    private async Task<UserInfoDto?> LoadCurrentUserAsync()
    {
        try
        {
            var token = _tokenService.AccessToken;

            // No usable token (first load, or a page reload): the refresh cookie is the only way to tell if there is a session
            if (string.IsNullOrEmpty(token) || TokenService.IsTokenExpired(token))
            {
                if (!await RefreshTokenAsync(token))
                {
                    return null;
                }

                token = _tokenService.AccessToken;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            using var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            _currentUser = await response.Content.ReadFromJsonAsync<UserInfoDto>(JsonOptions);

            return _currentUser;
        }
        catch (Exception ex) when (IsCallFailure(ex))
        {
            _logger.LogWarning(ex, "Could not load the signed-in user");
            return null;
        }
    }

    /// <summary>
    /// Gets a new access token using the refresh cookie. If another caller already replaced <paramref name="staleAccessToken"/>
    /// while we waited for the lock, no second refresh is made (refresh tokens are single-use).
    /// </summary>
    public async Task<bool> RefreshTokenAsync(string? staleAccessToken = null)
    {
        await _refreshLock.WaitAsync();
        try
        {
            var current = _tokenService.AccessToken;
            if (!string.IsNullOrEmpty(current) && current != staleAccessToken && !TokenService.IsTokenExpired(current))
            {
                return true;
            }

            using var response = await _httpClient.PostAsync("/api/auth/refresh", null);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    // No session, or it is over (expired, revoked or replayed)
                    SignOutLocally();
                }

                return false;
            }

            return await AcceptTokenAsync(response) != null;
        }
        catch (Exception ex) when (IsCallFailure(ex))
        {
            _logger.LogWarning(ex, "Refreshing the session failed");
            return false;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<AuthTokenResponse?> AcceptTokenAsync(HttpResponseMessage response)
    {
        var authResponse = await response.Content.ReadFromJsonAsync<AuthTokenResponse>(JsonOptions);
        if (authResponse != null)
        {
            _tokenService.SetAccessToken(authResponse.AccessToken);
        }

        return authResponse;
    }

    public async Task LogoutAsync()
    {
        try
        {
            using var response = await _httpClient.PostAsync("/api/auth/logout", null);
        }
        catch (Exception ex) when (IsCallFailure(ex))
        {
            // The local session is cleared regardless
            _logger.LogWarning(ex, "The API could not be told about the sign-out");
        }
        finally
        {
            SignOutLocally();
        }
    }

    private void SignOutLocally()
    {
        // Only a real sign-out is a change; "there was no session" (the first look at a fresh page) must not raise it, or
        // anything that reacts by looking for the session again would loop
        var hadSession = _currentUser != null || _tokenService.AccessToken != null;

        _tokenService.Clear();
        _currentUser = null;

        if (hadSession)
        {
            Changed?.Invoke();
        }
    }

    private static bool IsCallFailure(Exception ex)
    {
        return ex is HttpRequestException or TaskCanceledException or JsonException;
    }
}
