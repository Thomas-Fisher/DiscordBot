using System.Net;
using System.Text;
using DiscordBot.Application.Errors;
using DiscordBot.Application.Options;
using DiscordBot.Infrastructure.Auth;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;

namespace DiscordBot.Tests.Unit.Infrastructure;

public class DiscordOAuthClientTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        public List<HttpRequestMessage> Requests { get; } = new();

        public List<string> Bodies { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            Bodies.Add(request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            return _respond(request);
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private static DiscordOAuthClient Create(StubHandler handler)
    {
        var options = Options.Create(new DiscordOptions
        {
            ClientId = "client",
            ClientSecret = "secret",
            RedirectUri = "http://localhost/callback",
            ApiBaseUrl = "https://discord.test/api"
        });

        return new DiscordOAuthClient(new HttpClient(handler), options, NullLogger<DiscordOAuthClient>.Instance);
    }

    [Fact]
    public async Task ExchangeCode_DiscordAcceptsTheCode_ReturnsTheTokensAndSendsTheClientCredentials()
    {
        var handler = new StubHandler(_ => Json("{\"access_token\":\"tok\",\"refresh_token\":\"ref\",\"expires_in\":3600,\"token_type\":\"Bearer\"}"));

        var token = await Create(handler).ExchangeCodeAsync("the-code");

        Assert.Equal("tok", token);
        Assert.Equal(new Uri("https://discord.test/api/oauth2/token"), handler.Requests.Single().RequestUri);
        var sent = handler.Bodies.Single();
        Assert.Contains("code=the-code", sent);
        Assert.Contains("grant_type=authorization_code", sent);
        Assert.Contains("client_id=client", sent);
    }

    [Fact]
    public async Task ExchangeCode_DiscordRejectsTheCode_ReturnsNull()
    {
        var handler = new StubHandler(_ => Json("{\"error\":\"invalid_grant\"}", HttpStatusCode.BadRequest));

        var token = await Create(handler).ExchangeCodeAsync("bad");

        Assert.Null(token);
    }

    [Fact]
    public async Task GetUser_ValidToken_ReturnsTheUserAndSendsTheTokenAsABearerToken()
    {
        var handler = new StubHandler(_ => Json("{\"id\":\"42\",\"username\":\"Thomas\",\"avatar\":\"abc\"}"));

        var user = await Create(handler).GetUserAsync("access");

        Assert.Equal(("42", "Thomas"), (user.Id, user.Username));
        var request = handler.Requests.Single();
        Assert.Equal(("Bearer", "access"), (request.Headers.Authorization!.Scheme, request.Headers.Authorization.Parameter));
        Assert.Equal("/api/users/@me", request.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetUser_DiscordFails_Throws()
    {
        var handler = new StubHandler(_ => Json("{}", HttpStatusCode.Unauthorized));

        await Assert.ThrowsAsync<DiscordUnavailableException>(() => Create(handler).GetUserAsync("access"));
    }

    [Fact]
    public async Task GetGuilds_ValidToken_ReturnsTheGuildsWithTheirOwnerFlagAndPermissions()
    {
        var handler = new StubHandler(_ => Json("[{\"id\":\"1\",\"name\":\"One\",\"owner\":true,\"permissions\":\"8\"},{\"id\":\"2\",\"name\":\"Two\",\"owner\":false,\"permissions\":\"0\"}]"));

        var guilds = await Create(handler).GetGuildsAsync("access");

        Assert.Equal(2, guilds.Count);
        Assert.Equal((true, 8L), (guilds[0].Owner, guilds[0].Permissions));
        Assert.Equal("/api/users/@me/guilds", handler.Requests.Single().RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task GetUser_CancelledByTheCaller_StopsInsteadOfCallingDiscord()
    {
        var handler = new StubHandler(_ => Json("{}"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create(handler).GetUserAsync("access", cts.Token));
        Assert.Empty(handler.Requests);
    }
}
