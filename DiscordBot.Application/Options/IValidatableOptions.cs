namespace DiscordBot.Application.Options;

public interface IValidatableOptions
{
    /// <summary>What is wrong with the settings, worded for whoever edits the configuration (empty when they are fine).</summary>
    IReadOnlyList<string> Validate();
}
