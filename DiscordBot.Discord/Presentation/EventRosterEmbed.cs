using Discord;
using DiscordBot.Application.Models;
using DiscordBot.Discord.Services.WoW;
using DiscordBot.Domain.Common;
using DiscordBot.Domain.Entities;
using DiscordBot.Domain.Wow;

namespace DiscordBot.Discord.Presentation;

/// <summary>
/// The raid roster as a message of its own, laid out like the signup message: the roster's tank, healer, melee and ranged totals,
/// then each group of five with spec icons. Mentions inside an embed do not notify anyone, so the message around it mentions the people.
/// </summary>
public static class EventRosterEmbed
{
    private const int InlineFieldsPerRow = 3;
    private const string BlankField = "\u200b";

    private static readonly string WidthPadding = new('\u2800', 20);

    public static Embed Build(Event evt, IReadOnlyList<PingRecipient> roster, Func<ulong, string?> usernameLookup)
    {
        var builder = new EmbedBuilder()
            .WithTitle($"📋 {evt.Title} roster")
            .WithDescription($"Starts {DiscordTimestamp.LongDateTime(evt.ScheduledTime)} ({DiscordTimestamp.Relative(evt.ScheduledTime)})")
            .WithColor(Color.Blue);

        builder.AddField($"Roster ({roster.Count})", $"{Summary(roster)} {WidthPadding}", inline: false);

        var groups = roster.GroupBy(r => r.Group ?? 0).OrderBy(g => g.Key).ToList();
        foreach (var group in groups)
        {
            var lines = group.Select(r => Line(r, usernameLookup)).ToList();
            builder.AddField($"Group {group.Key} ({lines.Count}/{RosterRules.GroupSize})", EventEmbedBuilder.JoinTruncated(lines, "\n"), inline: true);
        }

        // Empty inline fields complete the last row
        for (var filler = groups.Count % InlineFieldsPerRow; filler > 0 && filler < InlineFieldsPerRow; filler++)
        {
            builder.AddField(BlankField, BlankField, inline: true);
        }

        return builder.Build();
    }

    private static string Summary(IReadOnlyList<PingRecipient> roster)
    {
        var byRole = roster.ToLookup(r => WowCatalog.RoleOf(r.WowClass, r.WowSpec));

        return $"🛡️ Tanks **{byRole[WowRole.Tank].Count()}** · 💚 Healers **{byRole[WowRole.Healer].Count()}** · ⚔️ Melee **{byRole[WowRole.Melee].Count()}** · 🏹 Ranged **{byRole[WowRole.Ranged].Count()}**";
    }

    private static string Line(PingRecipient person, Func<ulong, string?> usernameLookup)
    {
        var name = usernameLookup(person.UserId) ?? $"<@{person.UserId}>";
        if (person.IsUnsure)
        {
            name += " 🤷";
        }

        return string.IsNullOrEmpty(person.WowClass) || string.IsNullOrEmpty(person.WowSpec)
            ? name
            : $"{WowEmotes.GetSpecEmoteName(person.WowClass, person.WowSpec)} {name}";
    }
}
