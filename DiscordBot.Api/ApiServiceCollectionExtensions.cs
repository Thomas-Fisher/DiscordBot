using DiscordBot.Contracts;
using System.Text;
using DiscordBot.Api.Middleware;
using DiscordBot.Application.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

namespace DiscordBot.Api;

public static class ApiServiceCollectionExtensions
{
    public const string CorsPolicy = "Frontend";

    public static IServiceCollection AddApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddApiAuthentication();
        services.AddApiCors(configuration);
        services.AddApiForwardedHeaders(configuration);
        services.AddApiRateLimiting(configuration);

        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();

        // Compress JSON responses (activity logs and event lists can be large). Enabled over HTTPS too: the API
        // authenticates with bearer tokens rather than cookies, so BREACH-style attacks have no secret to target.
        services.AddResponseCompression(options => options.EnableForHttps = true);

        services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.Converters.Add(new UtcDateTimeConverter());
                options.JsonSerializerOptions.Converters.Add(new DiscordIdConverter());
            });
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            // So the API can be tried out from Swagger UI: paste an access token into Authorize
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "The access token the dashboard receives when signing in."
            });
            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] });
        });

        return services;
    }

    private static void AddApiAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer();

        // Configured from the validated JwtOptions, so the signing key is the one that was checked at start-up
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
            {
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Value.Secret)),
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Value.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Value.Audience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };
            });

        // Anything that does not say otherwise needs a signed-in user, so a new endpoint cannot be left open by forgetting [Authorize]
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
    }

    private static void AddApiCors(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatedOptions<CorsSettings>(configuration, CorsSettings.SectionName);

        services.AddCors();
        services.AddOptions<CorsOptions>().Configure<IOptions<CorsSettings>>((cors, settings) =>
            cors.AddPolicy(CorsPolicy, policy => policy
                .WithOrigins(settings.Value.AllowedOrigins)
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials()));
    }

    private static void AddApiForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatedOptions<ForwardedHeadersSettings>(configuration, ForwardedHeadersSettings.SectionName);

        services.AddOptions<ForwardedHeadersOptions>().Configure<IOptions<ForwardedHeadersSettings>>((forwarded, settings) =>
        {
            forwarded.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            foreach (var proxy in settings.Value.KnownProxies)
            {
                forwarded.KnownProxies.Add(System.Net.IPAddress.Parse(proxy));
            }
        });
    }
}
