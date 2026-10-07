using Discord;
using DiscordBot.Discord.Constants;
using DiscordBot.Discord.Services.WoW;
using DiscordBot.Domain.Entities;
using DiscordBot.Domain.Enums;
using DiscordBot.Domain.Wow;

namespace DiscordBot.Discord.Presentation;

public static class EventEmbedBuilder
{
    // Discord limits: 25 fields per embed, 1024 characters per field value, and 6000 characters across the whole embed
    public const int MaxFieldValueLength = 1024;
    public const int MaxEmbedLength = 6000;

    // A big raid can overflow the whole embed even when every field fits. Then the lists are cut shorter, step by step, until it fits.
    private static readonly int[] FieldLimits = { MaxFieldValueLength, 512, 256, 128, 64 };

    // An embed is only as wide as its widest line, so invisible characters (Braille blanks) stretch it towards Discord's maximum
    private const int InlineFieldsPerRow = 3;
    private const string BlankField = "\u200b";

    private static readonly string WidthPadding = new('\u2800', 20);

    // The names shown when hovering over a count have to fit, with their link, in the 1024 characters of the summary field
    private const int MaxTooltipLength = 70;

    /// <summary>
    /// Builds the embed. With <paramref name="messageUrl"/>, the link to the announcement message itself, the role counts become
    /// links to it whose hover text lists the people counted (Discord shows a link's text on hover, on desktop); without it they
    /// are plain numbers.
    /// </summary>
    public static Embed Build(Event evt, IReadOnlyCollection<EventSignup> signups, Func<ulong, string?> usernameLookup, string? messageUrl = null)
    {
        EmbedBuilder builder = null!;

        foreach (var fieldLimit in FieldLimits)
        {
            builder = evt.Type == EventType.Generic
                ? BuildGeneric(evt, signups, fieldLimit)
                : BuildWow(evt, signups, usernameLookup, fieldLimit, messageUrl);

            if (builder.Length <= MaxEmbedLength)
            {
                break;
            }
        }

        return builder.Build();
    }

    private static EmbedBuilder BuildGeneric(Event evt, IReadOnlyCollection<EventSignup> signups, int fieldLimit)
    {
        var signedUp = SignupsWith(signups, SignupStatus.SignedUp);
        var signedOff = SignupsWith(signups, SignupStatus.SignedOff);
        var unsure = SignupsWith(signups, SignupStatus.Unsure);

        var fields = WhenFields(evt);

        fields.Add(("📌 Event Type", evt.Type.ToString()));

        if (evt.IsRecurring)
        {
            fields.Add(("🔄 Recurrence", evt.RecurrencePattern?.ToString() ?? "N/A"));
        }

        if (evt.MentionRoleIds.Count > 0)
        {
            fields.Add(("📣 Pings", JoinTruncated(evt.MentionRoleIds.Select(id => $"<@&{id}>"), " ", fieldLimit)));
        }

        var builder = NewEmbed(evt, UserMessages.EventFooter);

        foreach (var (name, value) in fields)
        {
            builder.AddField(name, value, inline: true);
        }

        builder.AddField($"{SignupEmojis.SignedUp} Signed Up ({signedUp.Count})", MentionList(signedUp, fieldLimit), inline: false);
        builder.AddField($"{SignupEmojis.Unsure} Unsure ({unsure.Count})", MentionList(unsure, fieldLimit), inline: false);
        builder.AddField($"{SignupEmojis.SignedOff} Signed Off ({signedOff.Count})", MentionList(signedOff, fieldLimit), inline: false);
        builder.AddField("🆔 Event ID", $"`{evt.EventId}`", inline: false);

        return builder;
    }

    private static List<(string Name, string Value)> WhenFields(Event evt)
    {
        var fields = new List<(string Name, string Value)>
        {
            ("📅 Date", $"{DiscordTimestamp.LongDate(evt.ScheduledTime)}\n{DiscordTimestamp.Relative(evt.ScheduledTime)}"),
            ("🕐 Time", DiscordTimestamp.ShortTime(evt.ScheduledTime))
        };

        if (evt.DurationMinutes is { } duration)
        {
            var ends = DiscordTimestamp.ShortTime(evt.ScheduledTime.AddMinutes(duration));
            fields.Add(("⏱️ Duration", $"{DurationText.Format(duration)} (until {ends})"));
        }

        return fields;
    }

    /// <summary>
    /// WoW layout: a tank/healer/DPS headline, signups grouped by class with spec icons, then the unsure and signed-off. Pinged
    /// roles are the message text above the embed, so they are not repeated.
    /// </summary>
    private static EmbedBuilder BuildWow(Event evt, IReadOnlyCollection<EventSignup> signups, Func<ulong, string?> usernameLookup, int fieldLimit, string? messageUrl)
    {
        var signedUp = SignupsWith(signups, SignupStatus.SignedUp);
        var unsure = SignupsWith(signups, SignupStatus.Unsure);
        var signedOff = SignupsWith(signups, SignupStatus.SignedOff);

        var withSpec = signedUp.Where(HasSpec).ToList();
        var classGroups = withSpec
            .GroupBy(s => s.WowClass!.ToLowerInvariant())
            .OrderBy(g => WowCatalog.DisplayName(g.Key))
            .ToList();
        var withoutSpec = signedUp.Where(s => !HasSpec(s)).ToList();

        var builder = NewEmbed(evt, WowFooter(evt));

        foreach (var (name, value) in WhenFields(evt))
        {
            builder.AddField(name, value, inline: true);
        }

        builder.AddField($"{SignupEmojis.SignedUp} Signed Up ({signedUp.Count})", $"{RoleSummary(withSpec, usernameLookup, messageUrl)} {WidthPadding}", inline: false);

        foreach (var group in classGroups)
        {
            var heading = $"{WowEmotes.GetClassEmoteName(group.Key)} {WowCatalog.DisplayName(group.Key)} ({group.Count()})";
            builder.AddField(heading, LineList(group, usernameLookup, fieldLimit), inline: true);
        }

        CompleteRow(builder, classGroups.Count);

        if (withoutSpec.Count > 0)
        {
            builder.AddField($"❔ No spec chosen ({withoutSpec.Count})", LineList(withoutSpec, usernameLookup, fieldLimit), inline: false);
        }

        builder.AddField($"{SignupEmojis.Unsure} Unsure ({unsure.Count})", LineList(unsure, usernameLookup, fieldLimit), inline: true);
        builder.AddField($"{SignupEmojis.SignedOff} Signed Off ({signedOff.Count})", LineList(signedOff, usernameLookup, fieldLimit), inline: true);

        return builder;
    }

    // Empty inline fields complete the last row so whatever follows starts on a row of its own
    private static void CompleteRow(EmbedBuilder builder, int inlineFieldsAdded)
    {
        for (var filler = 0; filler < FillersFor(inlineFieldsAdded); filler++)
        {
            builder.AddField(BlankField, BlankField, inline: true);
        }
    }

    private static int FillersFor(int inlineFieldsAdded)
    {
        var onLastRow = inlineFieldsAdded % InlineFieldsPerRow;

        return onLastRow == 0 ? 0 : InlineFieldsPerRow - onLastRow;
    }

    private static bool HasSpec(EventSignup signup)
    {
        return WowCatalog.RoleOf(signup.WowClass, signup.WowSpec) != null;
    }

    private static string RoleSummary(List<EventSignup> withSpec, Func<ulong, string?> usernameLookup, string? messageUrl)
    {
        var byRole = withSpec.ToLookup(s => WowCatalog.RoleOf(s.WowClass, s.WowSpec));
        var tanks = byRole[WowRole.Tank].ToList();
        var healers = byRole[WowRole.Healer].ToList();
        var melee = byRole[WowRole.Melee].ToList();
        var ranged = byRole[WowRole.Ranged].ToList();

        string Names(List<EventSignup> people) => TooltipNames(people.OrderBy(s => s.SignupOrder).Select(s => usernameLookup(s.UserId) ?? $"User {s.UserId}").ToList());

        var dpsHover = string.Join(" · ", new[] { melee.Count > 0 ? $"Melee: {Names(melee)}" : null, ranged.Count > 0 ? $"Ranged: {Names(ranged)}" : null }.Where(part => part != null));

        return $"🛡️ Tanks {HoverCount(tanks.Count, Names(tanks), messageUrl)} · 💚 Healers {HoverCount(healers.Count, Names(healers), messageUrl)} · ⚔️ DPS {HoverCount(melee.Count + ranged.Count, dpsHover, messageUrl)} (Melee {melee.Count} · Ranged {ranged.Count})";
    }

    /// <summary>The count in bold, as a link to the message with the names as its hover text. A count of nothing has nobody to list.</summary>
    private static string HoverCount(int count, string names, string? messageUrl)
    {
        return messageUrl == null || count == 0
            ? $"**{count}**"
            : $"[**{count}**]({messageUrl} \"{names.Replace('"', '\'')}\")";
    }

    private static string TooltipNames(List<string> names)
    {
        var shown = new List<string>();
        var length = 0;

        foreach (var name in names)
        {
            var next = length + (shown.Count > 0 ? 2 : 0) + name.Length;
            if (next > MaxTooltipLength && shown.Count > 0)
            {
                return $"{string.Join(", ", shown)}, +{names.Count - shown.Count} more";
            }

            shown.Add(name);
            length = next;
        }

        return string.Join(", ", shown);
    }

    private static EmbedBuilder NewEmbed(Event evt, string footer)
    {
        var cancelled = evt.Status == EventStatus.Cancelled;

        return new EmbedBuilder()
            .WithTitle(cancelled ? $"❌ CANCELLED: {evt.Title}" : evt.Title)
            .WithDescription(evt.Description)
            .WithColor(cancelled ? Color.Red : Color.Blue)
            .WithFooter(cancelled ? "This event has been cancelled." : footer);
    }

    private static string WowFooter(Event evt)
    {
        var repeats = evt.IsRecurring ? $" · 🔄 {evt.RecurrencePattern}" : "";

        return $"{evt.Type}{repeats} · ID {evt.EventId}\n{UserMessages.WowEventFooter}";
    }

    private static List<EventSignup> SignupsWith(IReadOnlyCollection<EventSignup> signups, SignupStatus status)
    {
        return signups.Where(s => s.Status == status).OrderBy(s => s.SignupOrder).ToList();
    }

    private static string LineList(IEnumerable<EventSignup> group, Func<ulong, string?> usernameLookup, int fieldLimit)
    {
        var lines = group.Select(s => Line(s, usernameLookup)).ToList();

        return lines.Count == 0 ? UserMessages.SignupPlaceholder : JoinTruncated(lines, "\n", fieldLimit);
    }

    private static string Line(EventSignup signup, Func<ulong, string?> usernameLookup)
    {
        var name = usernameLookup(signup.UserId) ?? $"<@{signup.UserId}>";

        return string.IsNullOrEmpty(signup.WowClass) || string.IsNullOrEmpty(signup.WowSpec)
            ? name
            : $"{WowEmotes.GetSpecEmoteName(signup.WowClass, signup.WowSpec)} {name}";
    }

    private static string MentionList(IReadOnlyCollection<EventSignup> signups, int fieldLimit)
    {
        return signups.Count == 0
            ? UserMessages.SignupPlaceholder
            : JoinTruncated(signups.Select(s => $"<@{s.UserId}>"), ", ", fieldLimit);
    }

    /// <summary>
    /// Joins the parts, cutting cleanly between items (never mid-mention) with a "+N more" note when they don't fit.
    /// </summary>
    public static string JoinTruncated(IEnumerable<string> parts, string separator, int maxLength = MaxFieldValueLength)
    {
        var items = parts.ToList();
        var full = string.Join(separator, items);
        if (full.Length <= maxLength)
        {
            return full;
        }

        var included = new List<string>();
        var length = 0;
        for (var i = 0; i < items.Count; i++)
        {
            var moreNote = $"{separator}… +{items.Count - i} more";
            var added = (included.Count == 0 ? 0 : separator.Length) + items[i].Length;

            if (length + added + moreNote.Length > maxLength)
            {
                return string.Join(separator, included) + $"{separator}… +{items.Count - included.Count} more";
            }

            included.Add(items[i]);
            length += added;
        }

        return full;
    }
}
