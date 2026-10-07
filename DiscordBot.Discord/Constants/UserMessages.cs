namespace DiscordBot.Discord.Constants;

public static class UserMessages
{
    public const string EventNotFound = "❌ Event not found.";
    public const string SpecSelectionExpired = "Spec selection expired. Please click the class emoji again.";
    public const string InvalidSpecSelectionFormat = "Invalid spec selection format.";
    public const string PleaseSelectSpec = "Please select a spec.";
    public const string InvalidInteractionType = "Invalid interaction type.";
    public const string SpecSelectionNotForYou = "This spec selection is not for you.";
    public const string InsufficientPermissions = "❌ You don't have permission to perform this action. Only the server owner and people with the Administrator or Manage Server permission can create events.";
    public const string GuildOnly = "❌ This command can only be used in a guild.";
    public const string EventNotInGuild = "❌ This event doesn't belong to this guild.";
    public const string NotEventCreator = "❌ You can only manage events you created, or you must be a guild admin/owner.";
    public const string ErrorProcessingRequest = "❌ An error occurred processing your request.";


    public const string EventCreationExpired = "❌ That event form expired. Please start again with the command.";
    public const string EventFooter = $"React {SignupEmojis.SignedUp} to sign up, {SignupEmojis.Unsure} for unsure, {SignupEmojis.SignedOff} to sign off";
    public const string WowEventFooter = $"React with your class to sign up, {SignupEmojis.ResetSpec} to change your spec, {SignupEmojis.Unsure} for unsure, {SignupEmojis.SignedOff} to sign off";
    public const string ReactionsNotProcessed = "Your reactions on **{0}** weren't processed: you reacted with more than one option while the bot was offline, so it can't tell which one you meant. Please react again with the one you want. Any earlier sign-up you had is unchanged.";
    public const string SignupPlaceholder = "None yet";
}
