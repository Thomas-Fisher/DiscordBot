using Microsoft.Extensions.Options;

namespace DiscordBot.Application.Options;

public sealed class OptionsValidator<TOptions> : IValidateOptions<TOptions>
    where TOptions : class, IValidatableOptions
{
    public ValidateOptionsResult Validate(string? name, TOptions options)
    {
        var errors = options.Validate();

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
