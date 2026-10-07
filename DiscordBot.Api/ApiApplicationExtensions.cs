using Discord.Interactions;
using DiscordBot.Api.Middleware;
using DiscordBot.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;

namespace DiscordBot.Api;

public static class ApiApplicationExtensions
{
    /// <summary>The request pipeline. Order matters; the non-obvious placements are commented where they happen.</summary>
    public static WebApplication UseApiPipeline(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        if (app.Services.GetRequiredService<IOptions<ForwardedHeadersSettings>>().Value.Enabled)
        {
            app.UseForwardedHeaders();
        }

        app.UseSerilogRequestLogging();   // after forwarded headers, so the logged client address is the real one
        app.UseExceptionHandler();
        app.UseResponseCompression();

        // Development runs on plain http, where redirecting to https only produces a warning per request
        if (!app.Environment.IsDevelopment())
        {
            app.UseHttpsRedirection();
        }

        app.UseCors(ApiServiceCollectionExtensions.CorsPolicy);
        app.UseAuthentication();
        app.UseRateLimiter();   // after authentication so a signed-in caller is counted as themselves, after CORS so a 429 is readable by the browser
        app.UseAuthorization();

        return app;
    }

    public static WebApplication MapApiEndpoints(this WebApplication app)
    {
        app.MapControllers();
        app.MapHealthChecks("/health", new HealthCheckOptions { ResponseWriter = HealthResponseWriter.WriteAsync }).AllowAnonymous();
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false, ResponseWriter = HealthResponseWriter.WriteAsync }).AllowAnonymous();

        return app;
    }

    /// <summary>Applies pending migrations. A database that is still starting is waited for, a few times, before giving up.</summary>
    public static async Task MigrateDatabaseAsync(this WebApplication app)
    {
        const int attempts = 5;
        var logger = app.Services.GetRequiredService<ILogger<Program>>();

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var scope = app.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                await db.Database.MigrateAsync();
                return;
            }
            catch (Exception ex) when (attempt < attempts && ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not apply the database migrations (attempt {Attempt} of {Attempts}); trying again in 5 seconds", attempt, attempts);
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
        }
    }

    /// <summary>Finds the slash command modules in the Discord project. If one is broken the app does not start: running with no commands would look healthy and do nothing.</summary>
    public static async Task RegisterInteractionModulesAsync(this WebApplication app)
    {
        var interactionService = app.Services.GetRequiredService<InteractionService>();
        var logger = app.Services.GetRequiredService<ILogger<Program>>();

        await interactionService.AddModulesAsync(typeof(DiscordBot.Discord.SlashCommands.ModuleBase).Assembly, app.Services);

        logger.LogInformation("Loaded {ModuleCount} interaction modules", interactionService.Modules.Count);
        foreach (var module in interactionService.Modules)
        {
            logger.LogDebug("Module {ModuleName}: {Commands}", module.Name, string.Join(", ", module.SlashCommands.Select(command => "/" + command.Name)));
        }
    }
}
