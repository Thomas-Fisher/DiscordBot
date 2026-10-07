using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace DiscordBot.Web.Services;

/// <summary>
/// Makes the browser send (and accept) cookies on calls to the API, which lives on a different origin from the dashboard.
/// The refresh token is an HttpOnly cookie, so without this the refresh call would never carry it.
/// </summary>
public class CookieCredentialsHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        return base.SendAsync(request, cancellationToken);
    }
}
