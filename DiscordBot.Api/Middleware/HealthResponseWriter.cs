using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace DiscordBot.Api.Middleware;

/// <summary>
/// Writes the health report as JSON: the overall status for everyone, the individual checks (which name servers and topics)
/// only in Development or for a signed-in caller.
/// </summary>
public static class HealthResponseWriter
{
    private static readonly DateTime StartedAt = DateTime.UtcNow;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        var environment = context.RequestServices.GetRequiredService<IWebHostEnvironment>();
        var showDetails = environment.IsDevelopment() || context.User.Identity?.IsAuthenticated == true;

        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(JsonSerializer.Serialize(Build(report, environment.EnvironmentName, showDetails), Json));
    }

    public static object Build(HealthReport report, string environmentName, bool showDetails)
    {
        var summary = new Dictionary<string, object?>
        {
            ["status"] = report.Status.ToString(),
            ["environment"] = environmentName,
            ["version"] = Assembly.GetEntryAssembly()?.GetName().Version?.ToString(),
            ["startedAt"] = StartedAt,
            ["uptimeSeconds"] = (long)(DateTime.UtcNow - StartedAt).TotalSeconds,
            ["checkedInMs"] = (long)report.TotalDuration.TotalMilliseconds
        };

        if (showDetails)
        {
            summary["checks"] = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new Dictionary<string, object?>
                {
                    ["status"] = entry.Value.Status.ToString(),
                    ["description"] = entry.Value.Description,
                    ["durationMs"] = (long)entry.Value.Duration.TotalMilliseconds,
                    ["data"] = entry.Value.Data.Count == 0 ? null : entry.Value.Data,
                    ["error"] = entry.Value.Exception?.Message
                });
        }

        return summary;
    }
}
