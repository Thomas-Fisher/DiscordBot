using System.Net;
using System.Net.Http.Headers;

namespace DiscordBot.Web.Services;

/// <summary>
/// Adds the access token to each API request, refreshing an expired one first and retrying once after a 401. Set per request,
/// not on the shared <see cref="HttpClient"/>, so concurrent calls cannot overwrite each other.
/// </summary>
public class AccessTokenHandler : DelegatingHandler
{
    private readonly TokenService _tokens;
    private readonly AuthService _auth;

    public AccessTokenHandler(TokenService tokens, AuthService auth)
    {
        _tokens = tokens;
        _auth = auth;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = _tokens.AccessToken;

        if (!string.IsNullOrEmpty(token) && TokenService.IsTokenExpired(token) && await _auth.RefreshTokenAsync(token))
        {
            token = _tokens.AccessToken;
        }

        // A request with a body can only be sent once, so keep a copy of the body in case it has to be sent again
        var body = request.Content == null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken);
        var retry = Copy(request, body);

        Authorize(request, token);
        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized && !string.IsNullOrEmpty(token) && await _auth.RefreshTokenAsync(token))
        {
            response.Dispose();
            Authorize(retry, _tokens.AccessToken);
            return await base.SendAsync(retry, cancellationToken);
        }

        retry.Dispose();
        return response;
    }

    private static void Authorize(HttpRequestMessage request, string? token)
    {
        request.Headers.Authorization = string.IsNullOrEmpty(token) ? null : new AuthenticationHeaderValue("Bearer", token);
    }

    private static HttpRequestMessage Copy(HttpRequestMessage request, byte[]? body)
    {
        var copy = new HttpRequestMessage(request.Method, request.RequestUri) { Version = request.Version };

        foreach (var header in request.Headers.Where(h => h.Key != "Authorization"))
        {
            copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (body != null)
        {
            copy.Content = new ByteArrayContent(body);

            foreach (var header in request.Content!.Headers)
            {
                copy.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return copy;
    }
}
