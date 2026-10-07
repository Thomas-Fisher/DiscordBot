using DiscordBot.Api;
using DiscordBot.Application;
using DiscordBot.Discord;
using DiscordBot.Infrastructure;
using Serilog;

// Before the host reads its configuration: in Development the settings in .env become environment variables
DotEnvFile.LoadForDevelopment();

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog(ApiTelemetry.ConfigureLogging);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication(builder.Configuration);
builder.Services.AddApiTelemetry(builder.Configuration);
builder.Services.AddDiscordBot();
builder.Services.AddApi(builder.Configuration);

var app = builder.Build();

await app.RegisterInteractionModulesAsync();
await app.MigrateDatabaseAsync();

app.UseApiPipeline();
app.MapApiEndpoints();

app.Run();

// Lets the integration tests start the whole application (WebApplicationFactory<Program>)
public partial class Program;
