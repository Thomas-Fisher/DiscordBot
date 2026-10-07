using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DiscordBot.Application.Options;

public static class OptionsRegistration
{
    public static OptionsBuilder<TOptions> AddValidatedOptions<TOptions>(this IServiceCollection services, IConfiguration configuration, string sectionName)
        where TOptions : class, IValidatableOptions
    {
        services.AddSingleton<IValidateOptions<TOptions>, OptionsValidator<TOptions>>();

        return services.AddOptions<TOptions>()
            .Bind(configuration.GetSection(sectionName))
            .ValidateOnStart();
    }
}
