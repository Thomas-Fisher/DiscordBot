using System.Net;
using System.Text;
using DiscordBot.Web.Services;

namespace DiscordBot.Tests.Unit.Web;

public class ApiServiceTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string?, HttpResponseMessage> _respond;

        public StubHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        public List<(string Path, string? Authorization, string? Body)> Calls { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Calls.Add((request.RequestUri!.AbsolutePath, request.Headers.Authorization?.ToString(), body));
            return _respond(request, body);
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private static string Jwt(long exp)
    {
        static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{B64("{\"alg\":\"none\"}")}.{B64($"{{\"exp\":{exp}}}")}.sig";
    }

    private static string FreshJwt() => Jwt(DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds());

    private static string ExpiredJwt() => Jwt(DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds());

    private static string AuthBody(string accessToken) =>
        "{\"accessToken\":\"" + accessToken + "\",\"expiresIn\":900,\"user\":{\"id\":\"1\",\"username\":\"Thomas\",\"guilds\":[]}}";

    [Theory]
    [InlineData("{\"error\":\"Title is required.\"}", "Title is required.")]
    [InlineData("{\"title\":\"An error occurred\",\"detail\":\"Specific detail\"}", "Specific detail")]
    [InlineData("\"Event not found\"", "Event not found")]
    public void DescribeFailure_BodyWithAMessage_UsesIt(string body, string expected)
    {
        Assert.Equal(expected, ApiResult.DescribeFailure(HttpStatusCode.BadRequest, body));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "session has expired")]
    [InlineData(HttpStatusCode.Forbidden, "permission")]
    [InlineData(HttpStatusCode.NotFound, "could not be found")]
    [InlineData(HttpStatusCode.TooManyRequests, "Too many requests")]
    [InlineData(HttpStatusCode.InternalServerError, "problem")]
    public void DescribeFailure_NoUsableBody_FallsBackToAMessageForTheStatus(HttpStatusCode status, string expectedFragment)
    {
        // A 403 from the API has an empty body, and a proxy error page is HTML
        Assert.Contains(expectedFragment, ApiResult.DescribeFailure(status, ""));
        Assert.Contains(expectedFragment, ApiResult.DescribeFailure(status, "<html>Bad gateway</html>"));
    }

    private static ApiService Create(StubHandler handler)
    {
        return new ApiService(new HttpClient(handler) { BaseAddress = new Uri("http://api.test") });
    }

    [Fact]
    public async Task UpdateEvent_ApiRejectsTheRequest_ReturnsTheApisMessage()
    {
        var api = Create(new StubHandler((_, _) => Json("{\"error\":\"Title must be at least 3 characters.\"}", HttpStatusCode.BadRequest)));

        var result = await api.UpdateEventAsync(1, "ab", "", null, 0, new List<ulong>());

        Assert.False(result.IsSuccess);
        Assert.Equal("Title must be at least 3 characters.", result.ErrorMessage);
        Assert.Equal(HttpStatusCode.BadRequest, result.Status);
    }

    [Fact]
    public async Task GetGuilds_ApiCannotBeReached_ReturnsAFailureInsteadOfThrowing()
    {
        var api = Create(new StubHandler((_, _) => throw new HttpRequestException("connection refused")));

        var result = await api.GetGuildsAsync();

        Assert.False(result.IsSuccess);
        Assert.Null(result.Status);
        Assert.Contains("Could not reach the server", result.ErrorMessage);
    }

    [Fact]
    public async Task GetGuilds_ApiSendsSomethingUnexpected_ReturnsAFailureInsteadOfThrowing()
    {
        var api = Create(new StubHandler((_, _) => Json("<not json>")));

        var result = await api.GetGuildsAsync();

        Assert.False(result.IsSuccess);
        Assert.Contains("could not read", result.ErrorMessage);
    }

    [Fact]
    public async Task GetGuilds_ValidResponse_ReturnsTheGuilds()
    {
        var api = Create(new StubHandler((_, _) => Json("[{\"id\":\"1\",\"name\":\"Guild\"}]")));

        var result = await api.GetGuildsAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal("Guild", Assert.Single(result.Value!).Name);
    }

    private static (ApiService Api, StubHandler Server, TokenService Tokens, StubHandler AuthServer) CreateWithSession(
        Func<HttpRequestMessage, string?, HttpResponseMessage> api, Func<HttpRequestMessage, string?, HttpResponseMessage> auth, string? accessToken)
    {
        var tokens = new TokenService();
        if (accessToken != null)
        {
            tokens.SetAccessToken(accessToken);
        }

        var authServer = new StubHandler(auth);
        var authService = new AuthService(new HttpClient(authServer) { BaseAddress = new Uri("http://api.test") }, tokens);

        var apiServer = new StubHandler(api);
        var handler = new AccessTokenHandler(tokens, authService) { InnerHandler = apiServer };

        return (new ApiService(new HttpClient(handler) { BaseAddress = new Uri("http://api.test") }), apiServer, tokens, authServer);
    }

    [Fact]
    public async Task Request_SignedIn_CarriesTheAccessTokenOnThatRequest()
    {
        var token = FreshJwt();
        var (api, server, _, _) = CreateWithSession((_, _) => Json("[]"), (_, _) => Json("{}", HttpStatusCode.Unauthorized), token);

        await api.GetGuildsAsync();

        Assert.Equal("Bearer " + token, Assert.Single(server.Calls).Authorization);
    }

    [Fact]
    public async Task Request_AccessTokenExpired_RefreshesFirstAndSendsTheNewToken()
    {
        var fresh = FreshJwt();
        var (api, server, _, auth) = CreateWithSession((_, _) => Json("[]"), (_, _) => Json(AuthBody(fresh)), ExpiredJwt());

        var result = await api.GetGuildsAsync();

        Assert.True(result.IsSuccess);
        Assert.Single(auth.Calls);
        Assert.Equal("Bearer " + fresh, Assert.Single(server.Calls).Authorization);
    }

    [Fact]
    public async Task Request_ApiAnswers401_RefreshesAndRepeatsTheRequestIncludingItsBody()
    {
        var fresh = FreshJwt();
        var first = true;
        var (api, server, _, auth) = CreateWithSession(
            (_, _) =>
            {
                if (first)
                {
                    first = false;
                    return Json("{}", HttpStatusCode.Unauthorized);
                }

                return Json("{}");
            },
            (_, _) => Json(AuthBody(fresh)),
            FreshJwt());

        var result = await api.UpdateEventAsync(5, "New title", "text", null, 0, new List<ulong>());

        Assert.True(result.IsSuccess);
        Assert.Single(auth.Calls);
        Assert.Equal(2, server.Calls.Count);
        Assert.Equal("Bearer " + fresh, server.Calls[1].Authorization);
        Assert.Contains("New title", server.Calls[1].Body);
        Assert.Equal(server.Calls[0].Body, server.Calls[1].Body);
    }

    [Fact]
    public async Task Request_RefreshFails_ReturnsTheUnauthorizedMessage()
    {
        var (api, server, _, _) = CreateWithSession((_, _) => Json("{}", HttpStatusCode.Unauthorized), (_, _) => Json("{}", HttpStatusCode.Unauthorized), FreshJwt());

        var result = await api.GetGuildsAsync();

        Assert.False(result.IsSuccess);
        Assert.Contains("session has expired", result.ErrorMessage);
        Assert.Single(server.Calls);
    }

    [Fact]
    public async Task Changed_SignInAndSignOut_IsRaisedForEach()
    {
        var tokens = new TokenService();
        var auth = new AuthService(new HttpClient(new StubHandler((_, _) => Json(AuthBody(FreshJwt())))) { BaseAddress = new Uri("http://api.test") }, tokens);
        var raised = 0;
        auth.Changed += () => raised++;

        await auth.HandleCallbackAsync("code");
        await auth.LogoutAsync();

        Assert.Equal(2, raised);
    }

    [Fact]
    public async Task Changed_NoSessionToBeginWith_IsNotRaisedSoListenersCannotLoop()
    {
        var auth = new AuthService(new HttpClient(new StubHandler((_, _) => Json("{}", HttpStatusCode.Unauthorized))) { BaseAddress = new Uri("http://api.test") }, new TokenService());
        var raised = 0;
        auth.Changed += () => raised++;

        var user = await auth.GetCurrentUserAsync();

        Assert.Null(user);
        Assert.Equal(0, raised);
    }
}
