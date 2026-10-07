using DiscordBot.Application.Options;
using DiscordBot.Application.Telemetry;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Sinks.OpenTelemetry;

namespace DiscordBot.Api;

public static class ApiTelemetry
{
    private const string ServiceName = "discordbot";

    public static IServiceCollection AddApiTelemetry(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatedOptions<TelemetrySettings>(configuration, TelemetrySettings.SectionName);

        var telemetry = configuration.GetSection(TelemetrySettings.SectionName).Get<TelemetrySettings>() ?? new TelemetrySettings();
        var builder = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(ServiceName))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(options => options.Filter = context => !context.Request.Path.StartsWithSegments("/health"))
                .AddHttpClientInstrumentation()
                .AddSource(BotTelemetry.Name)
                .AddSource("Npgsql"))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(BotTelemetry.Name)
                .AddMeter("Npgsql"));

        if (telemetry.Enabled)
        {
            builder.UseOtlpExporter(OtlpExportProtocol.Grpc, new Uri(telemetry.Endpoint));
        }

        return services;
    }

    public static void ConfigureLogging(HostBuilderContext context, IServiceProvider services, LoggerConfiguration logging)
    {
        logging
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext();

        var telemetry = context.Configuration.GetSection(TelemetrySettings.SectionName).Get<TelemetrySettings>() ?? new TelemetrySettings();
        if (telemetry.Enabled)
        {
            logging.WriteTo.OpenTelemetry(options =>
            {
                options.Endpoint = telemetry.Endpoint;
                options.Protocol = OtlpProtocol.Grpc;
                options.ResourceAttributes = new Dictionary<string, object> { ["service.name"] = ServiceName };
            });
        }
    }
}
