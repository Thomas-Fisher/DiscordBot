using System.Net;
using System.Text;
using DiscordBot.Web.Services;

namespace DiscordBot.Tests.Unit.Web;

public class DashboardSessionTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        private readonly TaskCompletionSource? _gate;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond, TaskCompletionSource? gate = null)
        {
            _respond = respond;
            _gate = gate;
        }

        public List<(HttpMethod Method, string Path, string? Authorization)> Calls { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls.Add((request.Method, request.RequestUri!.AbsolutePath, request.Headers.Authorization?.ToString()));

            // While the gate is closed the "network call" is still in flight, which is what lets concurrent callers overlap
            if (_gate != null)
            {
                await _gate.Task;
            }

            return _respond(request);
        }
    }

    private static string Jwt(long exp)
    {
        static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{B64("{\"alg\":\"none\"}")}.{B64($"{{\"exp\":{exp}}}")}.sig";
    }

    private static string FreshJwt() => Jwt(DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds());

    private static string ExpiredJwt() => Jwt(DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds());

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private static string AuthBody(string accessToken) =>
        "{\"accessToken\":\"" + accessToken + "\",\"expiresIn\":900,\"user\":{\"id\":\"1\",\"username\":\"Thomas\",\"guilds\":[]}}";

    private static (AuthService Auth, TokenService Tokens) Create(StubHandler handler)
    {
        var tokens = new TokenService();
        var auth = new AuthService(new HttpClient(handler) { BaseAddress = new Uri("http://api.test") }, tokens);
        return (auth, tokens);
    }

    [Fact]
    public async Task HandleCallback_ValidCode_KeepsTheAccessTokenInMemoryAndExposesTheUser()
    {
        var access = FreshJwt();
        var (auth, tokens) = Create(new StubHandler(_ => Json(AuthBody(access))));

        var signedIn = await auth.HandleCallbackAsync("code");

        Assert.True(signedIn);
        Assert.Equal(access, tokens.AccessToken);
        Assert.Equal("Thomas", (await auth.GetCurrentUserAsync())!.Username);
    }

    [Fact]
    public async Task GetCurrentUser_FreshPageLoadWithARefreshCookie_RestoresTheSessionWithoutAskingForALogin()
    {
        var access = FreshJwt();
        var handler = new StubHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/auth/refresh" => Json(AuthBody(access)),
            "/api/auth/me" => Json("{\"id\":\"1\",\"username\":\"Thomas\",\"guilds\":[]}"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        var (auth, tokens) = Create(handler);

        var user = await auth.GetCurrentUserAsync();

        Assert.Equal("Thomas", user!.Username);
        Assert.Equal(access, tokens.AccessToken);
        Assert.Equal(new[] { "/api/auth/refresh", "/api/auth/me" }, handler.Calls.Select(c => c.Path));
        Assert.Equal("Bearer " + access, handler.Calls[1].Authorization);
    }

    [Fact]
    public async Task GetCurrentUser_NoSession_ReturnsNoUserAndHoldsNoToken()
    {
        var (auth, tokens) = Create(new StubHandler(_ => Json("{}", HttpStatusCode.Unauthorized)));

        var user = await auth.GetCurrentUserAsync();

        Assert.Null(user);
        Assert.Null(tokens.AccessToken);
    }

    [Fact]
    public async Task GetCurrentUser_SeveralComponentsAskAtOnce_ShareOneSessionRestore()
    {
        var gate = new TaskCompletionSource();
        var handler = new StubHandler(_ => Json("{}", HttpStatusCode.Unauthorized), gate);
        var (auth, _) = Create(handler);

        var callers = Enumerable.Range(0, 4).Select(_ => auth.GetCurrentUserAsync()).ToList();
        gate.SetResult();
        await Task.WhenAll(callers);

        Assert.Single(handler.Calls, c => c.Path == "/api/auth/refresh");
    }

    [Fact]
    public async Task RefreshToken_SeveralCallersWithTheSameStaleToken_CallTheApiOnce()
    {
        var gate = new TaskCompletionSource();
        var handler = new StubHandler(_ => Json(AuthBody(FreshJwt())), gate);
        var (auth, tokens) = Create(handler);
        var stale = ExpiredJwt();
        tokens.SetAccessToken(stale);

        var callers = Enumerable.Range(0, 5).Select(_ => auth.RefreshTokenAsync(stale)).ToList();
        gate.SetResult();
        var results = await Task.WhenAll(callers);

        Assert.All(results, Assert.True);
        Assert.Single(handler.Calls);
    }

    [Fact]
    public async Task RefreshToken_ApiRejectsTheRefreshCookie_EndsTheSession()
    {
        var (auth, tokens) = Create(new StubHandler(_ => Json("{}", HttpStatusCode.Unauthorized)));
        var stale = ExpiredJwt();
        tokens.SetAccessToken(stale);

        var refreshed = await auth.RefreshTokenAsync(stale);

        Assert.False(refreshed);
        Assert.Null(tokens.AccessToken);
    }

    [Fact]
    public async Task Logout_SignedInUser_TellsTheApiToClearTheCookieAndForgetsTheToken()
    {
        var handler = new StubHandler(_ => Json(AuthBody(FreshJwt())));
        var (auth, tokens) = Create(handler);
        await auth.HandleCallbackAsync("code");

        await auth.LogoutAsync();

        Assert.Null(tokens.AccessToken);
        Assert.Contains(handler.Calls, c => c.Method == HttpMethod.Post && c.Path == "/api/auth/logout");
    }
}
