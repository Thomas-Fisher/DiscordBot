using DiscordBot.Application.EventHandlers;
using DiscordBot.Application.Interfaces;
using DiscordBot.Application.Options;
using DiscordBot.Application.Services;
using DiscordBot.Domain.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DiscordBot.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddValidatedOptions<DiscordOptions>(configuration, DiscordOptions.SectionName);
        services.AddValidatedOptions<EventReminderOptions>(configuration, EventReminderOptions.SectionName);
        services.AddValidatedOptions<RetentionOptions>(configuration, RetentionOptions.SectionName);
        services.AddValidatedOptions<RecurrenceOptions>(configuration, RecurrenceOptions.SectionName);

        // The shipped placeholder JWT secret is only acceptable in Development, so this one needs the environment
        services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>();
        services.AddOptions<JwtOptions>().Bind(configuration.GetSection(JwtOptions.SectionName)).ValidateOnStart();

        services.AddScoped<ISettingsService, SettingsService>();
        services.AddScoped<IEventService, EventService>();
        services.AddScoped<IEventCreator, EventCreator>();
        services.AddScoped<IEventPingService, EventPingService>();
        services.AddScoped<IEventScheduler, EventScheduler>();
        services.AddScoped<ISignupService, SignupService>();
        services.AddScoped<IActivityLogService, ActivityLogService>();

        services.AddScoped<IEventHandler<MessageReceivedEvent>, MessageLoggingEventHandler>();

        return services;
    }

    private sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
    {
        private readonly IHostEnvironment _environment;

        public JwtOptionsValidator(IHostEnvironment environment)
        {
            _environment = environment;
        }

        public ValidateOptionsResult Validate(string? name, JwtOptions options)
        {
            var errors = options.Validate(allowPlaceholderSecret: _environment.IsDevelopment());

            return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
        }
    }
}
