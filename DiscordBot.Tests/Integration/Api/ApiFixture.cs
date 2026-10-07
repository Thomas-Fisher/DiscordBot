using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using DiscordBot.Application.Interfaces;
using DiscordBot.Infrastructure.Persistence;
using DiscordBot.Tests.Integration.Postgres;
using DiscordBot.Tests.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;

namespace DiscordBot.Tests.Integration.Api;

/// <summary>
/// Starts the application (its full pipeline: authentication, CORS, rate limiting, JSON, exception handling) against a
/// throwaway PostgreSQL database, with the Discord connection switched off and a throwaway Kafka topic prefix on the shared test broker.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    public const string Issuer = "test-issuer";
    public const string Audience = "test-audience";
    public const string AllowedOrigin = "http://localhost:5000";

    private const string JwtSecret = "integration-test-secret-that-is-long-enough-1234567890";

    private static readonly Dictionary<string, string> Settings = new()
    {
        ["Jwt__Secret"] = JwtSecret,
        ["Jwt__Issuer"] = Issuer,
        ["Jwt__Audience"] = Audience,
        ["Discord__BotToken"] = "not-a-real-token",
        ["Discord__ClientId"] = "not-a-real-client-id",
        ["Discord__ClientSecret"] = "not-a-real-secret",
        ["Discord__Enabled"] = "false",
        ["OpenTelemetry__Endpoint"] = "",
        ["Cors__AllowedOrigins__0"] = AllowedOrigin
    };

    private readonly Dictionary<string, string> _brokerSettings = new();
    private readonly PostgresFixture _postgres = new();
    private WebApplicationFactory<Program>? _factory;

    public HashSet<ulong> ManagedGuilds { get; } = new();

    public async Task InitializeAsync()
    {
        await _postgres.InitializeAsync();

        var id = Guid.NewGuid().ToString("N")[..8];
        _brokerSettings["EventBus__Kafka__BootstrapServers"] = await TestContainers.KafkaBootstrapAsync();
        _brokerSettings["EventBus__Kafka__TopicPrefix"] = "api" + id;
        _brokerSettings["EventBus__Kafka__ConsumerGroupId"] = "api-group-" + id;

        // The application reads its configuration while it starts up, so it has to be in the environment before it does
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", _postgres.ConnectionString);
        foreach (var (key, value) in Settings.Concat(_brokerSettings))
        {
            Environment.SetEnvironmentVariable(key, value);
        }

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IGuildAccessService>();
                services.AddSingleton<IGuildAccessService>(new FakeGuildAccess(ManagedGuilds));
            });
        });

        _factory.CreateClient().Dispose();   // starts the application now, so a startup problem fails here
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();

        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", null);
        foreach (var key in Settings.Keys.Concat(_brokerSettings.Keys))
        {
            Environment.SetEnvironmentVariable(key, null);
        }

        await _postgres.DisposeAsync();
    }

    public HttpClient CreateClient(ulong? signedInUserId = null)
    {
        var client = _factory!.CreateClient();

        if (signedInUserId != null)
        {
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", AccessToken(signedInUserId.Value));
        }

        return client;
    }

    public AppDbContext CreateDbContext()
    {
        return _postgres.CreateContext();
    }

    private static string AccessToken(ulong userId)
    {
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: new[] { new Claim("discord_user_id", userId.ToString()), new Claim("discord_username", "Tester") },
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtSecret)), SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class FakeGuildAccess : IGuildAccessService
    {
        private readonly HashSet<ulong> _managed;

        public FakeGuildAccess(HashSet<ulong> managed)
        {
            _managed = managed;
        }

        public bool IsAvailable => true;

        public Task<bool> CanManageGuildAsync(ulong userId, ulong guildId) => Task.FromResult(_managed.Contains(guildId));

        public Task<IReadOnlyList<ulong>> GetManageableGuildIdsAsync(ulong userId) => Task.FromResult<IReadOnlyList<ulong>>(_managed.ToList());
    }
}
