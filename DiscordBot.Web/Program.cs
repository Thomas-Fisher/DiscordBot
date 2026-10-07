using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using DiscordBot.Web;
using DiscordBot.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

var apiUrl = new Uri(builder.Configuration["ApiUrl"] ?? "http://localhost:5001");

builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<GuildClock>();

// Credentials are included so the browser sends the HttpOnly refresh-token cookie to the API. The session client carries no
// access token (it is what obtains one); the API client adds the token to each request and refreshes it when it expires.
builder.Services.AddScoped(sp => new AuthService(
    NewClient(apiUrl),
    sp.GetRequiredService<TokenService>(),
    sp.GetRequiredService<ILogger<AuthService>>()));

builder.Services.AddScoped(sp => new ApiService(
    NewClient(apiUrl, new AccessTokenHandler(sp.GetRequiredService<TokenService>(), sp.GetRequiredService<AuthService>()))));

await builder.Build().RunAsync();

static HttpClient NewClient(Uri baseAddress, DelegatingHandler? outer = null)
{
    HttpMessageHandler handler = new CookieCredentialsHandler { InnerHandler = new HttpClientHandler() };

    if (outer != null)
    {
        outer.InnerHandler = handler;
        handler = outer;
    }

    return new HttpClient(handler) { BaseAddress = baseAddress };
}
