using Discord;

namespace DiscordBot.Discord.Constants;

public static class SignupEmojis
{
    public const string SignedUp = "✅";
    public const string Unsure = "🤷";
    public const string SignedOff = "❌";

    /// <summary>On WoW events: asks for the spec again, for the class the member is signed up as.</summary>
    public const string ResetSpec = "🔄";

    public static readonly string[] All = { SignedUp, SignedOff, Unsure, ResetSpec };

    /// <summary>
    /// The emote to react or un-react with. Goes through here so that any of these can become a custom emote (<c>&lt;:name:id&gt;</c>)
    /// by changing its constant alone.
    /// </summary>
    public static IEmote ToEmote(string emoji)
    {
        return Emote.TryParse(emoji, out var custom) ? custom : new Emoji(emoji);
    }
}
