using System.Text.Json;
using DiscordBot.Api.Middleware;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DiscordBot.Tests.Unit.Health;

public class HealthTests
{
    private static HealthReport ReportOf(params (string Name, HealthCheckResult Result)[] entries)
    {
        return new HealthReport(
            entries.ToDictionary(e => e.Name, e => new HealthReportEntry(e.Result.Status, e.Result.Description, TimeSpan.FromMilliseconds(5), e.Result.Exception, e.Result.Data)),
            TimeSpan.FromMilliseconds(12));
    }

    [Fact]
    public void Build_AnonymousCaller_GetsTheOverallStatusButNoInternalDetails()
    {
        var report = ReportOf(("eventBus", HealthCheckResult.Healthy("ok", new Dictionary<string, object> { ["bootstrapServers"] = "secret-host:9092" })));

        var json = JsonSerializer.Serialize(HealthResponseWriter.Build(report, "Production", showDetails: false));

        Assert.Contains("Healthy", json);
        Assert.DoesNotContain("secret-host", json);
        Assert.DoesNotContain("checks", json);
    }

    [Fact]
    public void Build_CallerAllowedToSeeDetails_ListsEachCheckWithItsStatusDescriptionAndData()
    {
        var report = ReportOf(
            ("database", HealthCheckResult.Healthy("up to date")),
            ("eventBus", HealthCheckResult.Degraded("topics missing", data: new Dictionary<string, object> { ["brokers"] = 1 })));

        var json = JsonSerializer.Serialize(HealthResponseWriter.Build(report, "Development", showDetails: true), new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"status\":\"Degraded\"", json);
        Assert.Contains("\"database\"", json);
        Assert.Contains("topics missing", json);
        Assert.Contains("\"brokers\":1", json);
    }
}
