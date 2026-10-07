using DiscordBot.Api;
using DiscordBot.Application;
using DiscordBot.Application.Options;
using DiscordBot.Infrastructure.EventBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;

namespace DiscordBot.Tests.Unit.Application;

public class OptionsValidationTests
{
    private static Dictionary<string, string?> ValidSettings() => new()
    {
        ["Discord:ClientId"] = "client",
        ["Discord:ClientSecret"] = "secret",
        ["Discord:RedirectUri"] = "http://localhost:5000/callback",
        ["Discord:BotToken"] = "token",
        ["Jwt:Secret"] = new string('s', 40),
        ["Jwt:Issuer"] = "issuer",
        ["Jwt:Audience"] = "audience"
    };

    private static T Resolve<T>(Dictionary<string, string?> settings, string environment = "Production")
        where T : class
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var host = new Mock<IHostEnvironment>();
        host.SetupGet(h => h.EnvironmentName).Returns(environment);

        var services = new ServiceCollection();
        services.AddSingleton(host.Object);
        services.AddApplication(configuration);

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<T>>().Value;
    }

    [Fact]
    public void Discord_MissingBotToken_StopsStartUpNamingTheSetting()
    {
        var settings = ValidSettings();
        settings.Remove("Discord:BotToken");

        var failure = Assert.Throws<OptionsValidationException>(() => Resolve<DiscordOptions>(settings));

        Assert.Contains("Discord:BotToken", failure.Message);
    }

    [Fact]
    public void Discord_SwitchedOff_NeedsNoBotToken()
    {
        var settings = ValidSettings();
        settings.Remove("Discord:BotToken");
        settings["Discord:Enabled"] = "false";

        Assert.False(Resolve<DiscordOptions>(settings).Enabled);
    }

    [Fact]
    public void Discord_ShippedPlaceholders_AreRefusedLikeMissingValues()
    {
        var settings = ValidSettings();
        settings["Discord:BotToken"] = "REPLACE-WITH-YOUR-BOT-TOKEN (Discord developer portal)";
        settings["Discord:ClientId"] = "REPLACE-WITH-YOUR-CLIENT-ID";
        settings["Discord:ClientSecret"] = "replace-with-your-client-secret";

        var failure = Assert.Throws<OptionsValidationException>(() => Resolve<DiscordOptions>(settings));

        Assert.Contains("Discord:BotToken", failure.Message);
        Assert.Contains("Discord:ClientId", failure.Message);
        Assert.Contains("Discord:ClientSecret", failure.Message);
    }

    [Fact]
    public void Discord_MissingOAuthSettings_ListsEveryOneThatIsMissing()
    {
        var settings = ValidSettings();
        settings.Remove("Discord:ClientId");
        settings.Remove("Discord:ClientSecret");

        var failure = Assert.Throws<OptionsValidationException>(() => Resolve<DiscordOptions>(settings));

        Assert.Contains("Discord:ClientId", failure.Message);
        Assert.Contains("Discord:ClientSecret", failure.Message);
    }

    [Fact]
    public void Jwt_PlaceholderSecret_IsRefusedOutsideDevelopmentOnly()
    {
        var settings = ValidSettings();
        settings["Jwt:Secret"] = "your-super-secret-jwt-key-min-32-characters-long";

        Assert.Throws<OptionsValidationException>(() => Resolve<JwtOptions>(settings, "Production"));
        Assert.NotNull(Resolve<JwtOptions>(settings, "Development"));
    }

    [Theory]
    [InlineData("EventReminders:CheckIntervalSeconds", "0")]
    public void EventReminders_OutOfRangeValue_StopsStartUp(string key, string value)
    {
        var settings = ValidSettings();
        settings[key] = value;

        Assert.Throws<OptionsValidationException>(() => Resolve<EventReminderOptions>(settings));
    }

    [Fact]
    public void Recurrence_NoCheckInterval_StopsStartUp()
    {
        var settings = ValidSettings();
        settings["Recurrence:CheckIntervalSeconds"] = "0";

        Assert.Throws<OptionsValidationException>(() => Resolve<RecurrenceOptions>(settings));
    }

    [Fact]
    public void Discord_CommandScope_DefaultsToGlobalAndCanBeSetToGuild()
    {
        var guild = ValidSettings();
        guild["Discord:CommandScope"] = "Guild";

        Assert.Equal(CommandScope.Global, Resolve<DiscordOptions>(ValidSettings()).CommandScope);
        Assert.Equal(CommandScope.Guild, Resolve<DiscordOptions>(guild).CommandScope);
    }

    [Fact]
    public void Retention_ZeroDays_MeansKeepForeverAndIsValid()
    {
        Assert.Empty(new RetentionOptions { MessageLogDays = 0, ActivityLogDays = 0, ProcessedEventDays = 0 }.Validate());
    }

    [Fact]
    public void Retention_NegativeDaysOrNoInterval_IsInvalid()
    {
        Assert.NotEmpty(new RetentionOptions { MessageLogDays = -1 }.Validate());
        Assert.NotEmpty(new RetentionOptions { IntervalHours = 0 }.Validate());
    }

    [Fact]
    public void EventBus_DefaultSettings_AreValid()
    {
        Assert.Empty(new EventBusOptions().Validate());
    }

    [Fact]
    public void EventBus_BrokerSettingsThatCannotWork_AreInvalid()
    {
        Assert.NotEmpty(new EventBusOptions { Kafka = { BootstrapServers = "" } }.Validate());
        Assert.NotEmpty(new EventBusOptions { Kafka = { Partitions = 0 } }.Validate());
        Assert.NotEmpty(new EventBusOptions { Kafka = { AutoOffsetReset = "Sometime" } }.Validate());
    }

    [Theory]
    [InlineData("*")]
    [InlineData("localhost:3000")]
    [InlineData("https://dashboard.example.com/app")]
    public void Cors_AnythingButAPlainOrigin_IsInvalid(string origin)
    {
        Assert.NotEmpty(new CorsSettings { AllowedOrigins = [origin] }.Validate());
    }

    [Fact]
    public void Cors_PlainOrigins_AreValid()
    {
        Assert.Empty(new CorsSettings { AllowedOrigins = ["http://localhost:5000", "https://dashboard.example.com"] }.Validate());
    }

    [Fact]
    public void ForwardedHeaders_ProxyThatIsNotAnIpAddress_IsInvalid()
    {
        Assert.NotEmpty(new ForwardedHeadersSettings { KnownProxies = ["not-an-ip"] }.Validate());
        Assert.Empty(new ForwardedHeadersSettings { KnownProxies = ["10.0.0.1", "::1"] }.Validate());
    }
}
