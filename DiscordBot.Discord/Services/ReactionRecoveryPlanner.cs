using DiscordBot.Discord.Services.WoW;
using DiscordBot.Domain.Entities;
using DiscordBot.Domain.Enums;
using DiscordBot.Discord.Constants;

namespace DiscordBot.Discord.Services;

/// <summary>
/// Decides what to do with reactions found after downtime: one new sign-up reaction is treated as just added; several cannot be
/// understood (which did they mean?), so they are removed and the person is asked to react again.
/// </summary>
public static class ReactionRecoveryPlanner
{
    private static readonly IReadOnlyDictionary<string, SignupStatus> StatusByEmoji = new Dictionary<string, SignupStatus>
    {
        [SignupEmojis.SignedUp] = SignupStatus.SignedUp,
        [SignupEmojis.Unsure] = SignupStatus.Unsure,
        [SignupEmojis.SignedOff] = SignupStatus.SignedOff
    };

    public record Replay(ulong UserId, string Emoji);

    public record Reject(ulong UserId, IReadOnlyList<string> Emojis);

    public record Plan(IReadOnlyList<Replay> Replays, IReadOnlyList<Reject> Rejects);

    public static Plan Create(Event evt, IReadOnlyCollection<EventSignup> signups, IReadOnlyDictionary<ulong, List<string>> emojisByUser)
    {
        var signupByUser = signups.ToDictionary(s => s.UserId);
        var replays = new List<Replay>();
        var rejects = new List<Reject>();

        foreach (var (userId, emojis) in emojisByUser.OrderBy(pair => pair.Key))
        {
            var signupEmojis = emojis.Where(e => IsSignupEmoji(evt, e)).ToList();

            // Other reactions go through the normal handling too, which removes anything that isn't allowed on the message
            replays.AddRange(emojis.Except(signupEmojis).Select(e => new Replay(userId, e)));

            // A reaction that matches the stored signup was handled before the bot went offline; only the others are new
            var signup = signupByUser.GetValueOrDefault(userId);
            var unhandled = signupEmojis.Where(e => !AlreadyReflected(e, signup)).ToList();

            if (unhandled.Count > 1)
            {
                rejects.Add(new Reject(userId, signupEmojis));
            }
            else if (unhandled.Count == 1)
            {
                replays.Add(new Replay(userId, unhandled[0]));
            }
        }

        return new Plan(replays, rejects);
    }

    private static bool IsSignupEmoji(Event evt, string emoji)
    {
        return StatusByEmoji.ContainsKey(emoji) || (evt.IsWow && !string.IsNullOrEmpty(WowEmotes.GetClassNameFromEmote(emoji)));
    }

    private static bool AlreadyReflected(string emoji, EventSignup? signup)
    {
        if (signup == null)
        {
            return false;
        }

        if (StatusByEmoji.TryGetValue(emoji, out var status))
        {
            return signup.Status == status;
        }

        return signup.Status == SignupStatus.SignedUp
            && string.Equals(signup.WowClass, WowEmotes.GetClassNameFromEmote(emoji), StringComparison.OrdinalIgnoreCase);
    }
}
