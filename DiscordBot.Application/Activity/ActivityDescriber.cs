using System.Text.Json;
using System.Text.RegularExpressions;
using DiscordBot.Application.Models;
using DiscordBot.Domain.Wow;

namespace DiscordBot.Application.Activity;

public sealed record ActivityRecord(string Action, string Entity, ulong? EntityId, ulong GuildId, ulong? UserId, string? ChangesJson, string? Source);

public sealed record ActivityDescription(string Icon, string Text)
{
    public override string ToString()
    {
        return $"{Icon} {Text}";
    }
}

/// <summary>
/// Turns an activity log entry into a sentence a person can read ("@Wez joined #General"). The same wording serves the Discord
/// log channel and the dashboard; <see cref="IActivityText"/> decides how names and links are written for each. Entries written
/// before a detail was recorded fall back to wording that does not need it.
/// </summary>
public static partial class ActivityDescriber
{
    public static ActivityDescription Describe(ActivityRecord record, IActivityText text)
    {
        var data = ChangeData.Parse(record.ChangesJson);
        var actor = record.UserId is { } id ? text.User(id) : record.Source is nameof(ActivityOrigin.System) or nameof(ActivityOrigin.Recurring) ? "The bot" : "Someone";

        return record.Action switch
        {
            "Created" => EventCreated(actor, data, text),
            "Updated" when record.Entity == nameof(ActivityEntity.GuildSettings) => SettingsChanged(actor, data, text),
            "Updated" => EventUpdated(actor, data, text),
            "Cancelled" => new("🚫", $"{actor} {text.Verb("cancelled")} {EventName(data, text)}"),
            "Deleted" => new("🗑️", $"{actor} {text.Verb("deleted")} {EventName(data, text)}"),
            "Completed" => new("🏁", $"{EventName(data, text)} has {text.Verb("finished")}"),
            "CancelledRecurring" => new("🚫", $"{actor} {text.Verb("cancelled")} the whole series of {EventName(data, text)}{(data.Number("cancelledCount") is { } count ? $" ({count} events)" : "")}"),
            "CreatedRecurring" => new("🔄", $"Next occurrence of {EventName(data, text)} {text.Verb("scheduled")}{(data.Time("nextScheduledTime") is { } next ? $" for {text.Time(next)}" : "")}"),
            "RosterUpdated" => new("📋", $"{actor} {text.Verb("set the roster")} for {EventName(data, text)}{(data.Number("people") is { } people ? $": {people} {(people == 1 ? "person" : "people")}" : "")}{(data.Number("groups") is { } groups ? $" in {groups} {(groups == 1 ? "group" : "groups")}" : "")}"),
            "PingSent" => PingSent(actor, data, text),
            "SignedUp" => SignedUp(record, actor, data, text),
            "SignedOff" => IsSelf(record, data)
                ? new("❌", $"{actor} {text.Verb("signed off")} from {EventName(data, text)}")
                : new("❌", $"{actor} {text.Verb("removed")} {text.User(data.Id("userId")!.Value)} from {EventName(data, text)}"),
            "MarkedUnsure" => IsSelf(record, data)
                ? new("🤷", $"{actor} {text.Verb("is unsure")} about {EventName(data, text)}")
                : new("🤷", $"{actor} {text.Verb("marked")} {text.User(data.Id("userId")!.Value)} as unsure for {EventName(data, text)}"),
            "UserJoined" => new("📥", $"{Member(record, text)} {text.Verb("joined")} the server"),
            "UserLeft" => new("📤", $"{Member(record, text)} {text.Verb("left")} the server"),
            "UserKicked" => new("👢", $"{Member(record, text)} {text.Verb("was kicked")}"),
            "UserBanned" => new("🔨", $"{Member(record, text)} {text.Verb("was banned")}"),
            "MessageUpdated" => MessageEdited(record, actor, data, text),
            "MessageDeleted" => MessageDeleted(record, data, text),
            "ReactionAdded" => Reaction(record, actor, data, text, added: true),
            "ReactionRemoved" => Reaction(record, actor, data, text, added: false),
            "ChannelCreated" => new("🆕", $"Channel {text.Channel(record.EntityId, data.Text("name"))} was {text.Verb("created")}"),
            "ChannelDeleted" => new("🗑️", $"Channel {text.Channel(null, data.Text("name"))} was {text.Verb("deleted")}"),
            "RoleCreated" => new("🆕", $"Role {text.Name(data.Text("name") ?? "(unnamed)")} was {text.Verb("created")}"),
            "RoleDeleted" => new("🗑️", $"Role {text.Name(data.Text("name") ?? "(unnamed)")} was {text.Verb("deleted")}"),
            "VoiceStateChanged" => Voice(actor, data, text),
            _ => new("📝", $"{actor} {Words(record.Action).ToLowerInvariant()} ({record.Entity})")
        };
    }

    /// <summary>Everyone an entry's sentence names, so the caller can look their names up once.</summary>
    public static IReadOnlyCollection<ulong> UserIds(ActivityRecord record)
    {
        var data = ChangeData.Parse(record.ChangesJson);
        var ids = new HashSet<ulong>();

        foreach (var id in new[] { record.UserId, data.Id("userId"), data.Id("authorId") })
        {
            if (id is { } found)
            {
                ids.Add(found);
            }
        }

        return ids;
    }

    private static ActivityDescription EventCreated(string actor, ChangeData data, IActivityText text)
    {
        var when = data.Time("scheduledTime") is { } time ? $" for {text.Time(time)}" : "";
        var repeats = data.Text("recurrence") is { } pattern ? $" (repeats: {Words(pattern).ToLowerInvariant()})" : "";

        return new("📅", $"{actor} {text.Verb("created")} {EventName(data, text)}{when}{repeats}");
    }

    private static ActivityDescription EventUpdated(string actor, ChangeData data, IActivityText text)
    {
        if (data.Id("movedToChannel") is { } to)
        {
            return new("📍", $"{actor} {text.Verb("moved")} {EventName(data, text)} from {text.Channel(data.Id("movedFromChannel"), null)} to {text.Channel(to, null)}");
        }

        var changed = new List<string>();
        foreach (var (key, label) in new[] { ("title", "title"), ("description", "description"), ("scheduledTime", "start time"), ("reminderMinutes", "reminder"), ("durationMinutes", "duration"), ("mentionRoleIds", "pinged roles") })
        {
            if (data.Has(key))
            {
                changed.Add(label);
            }
        }

        return new("✏️", $"{actor} {text.Verb("updated")} {EventName(data, text)}{(changed.Count > 0 ? ": " + string.Join(", ", changed) : "")}");
    }

    private static ActivityDescription SettingsChanged(string actor, ChangeData data, IActivityText text)
    {
        var changed = new List<string>();
        var settings = data.Has("changes") ? data.Child("changes") : data;

        if (data.Text("setting") == SettingKeys.TimeZone && data.Text("value") is { } zone)
        {
            changed.Add($"time zone to {zone}");
        }

        if (settings.Text(SettingKeys.TimeZone) is { } newZone)
        {
            changed.Add($"time zone to {newZone}");
        }

        if (settings.Has(SettingKeys.EchoChannel))
        {
            changed.Add(settings.Id(SettingKeys.EchoChannel) is { } channel && channel != 0 ? $"log channel to {text.Channel(channel, null)}" : "log channel (cleared)");
        }

        if (settings.Has(SettingKeys.EchoEnabled))
        {
            changed.Add(settings.Bool(SettingKeys.EchoEnabled) == true ? "log echo on" : "log echo off");
        }

        if (settings.Has(SettingKeys.DisabledAuditCategories))
        {
            changed.Add("which activities are recorded");
        }

        return new("⚙️", $"{actor} {text.Verb("changed")} the server settings{(changed.Count > 0 ? ": " + string.Join(", ", changed) : "")}");
    }

    private static ActivityDescription PingSent(string actor, ChangeData data, IActivityText text)
    {
        var people = data.Number("people") ?? 0;
        var who = data.Text("target") switch
        {
            "Roster" => "in the roster",
            "Standby" => "on standby",
            _ => "who had not responded"
        };

        return new("📣", $"{actor} {text.Verb("pinged")} {people} {(people == 1 ? "person" : "people")} {who} for {EventName(data, text)}");
    }

    private static ActivityDescription SignedUp(ActivityRecord record, string actor, ChangeData data, IActivityText text)
    {
        var details = data.Child("details");
        var suffix = "";
        if (details.Text("wowClass") is { } wowClass)
        {
            suffix = $" as {WowCatalog.DisplayName(wowClass)}{(details.Text("wowSpec") is { } spec ? $" ({Capitalised(spec)})" : "")}";
        }

        if (details.Number("order") is { } order)
        {
            suffix += $", #{order}";
        }

        return IsSelf(record, data)
            ? new("✅", $"{actor} {text.Verb("signed up")} to {EventName(data, text)}{suffix}")
            : new("✅", $"{actor} {text.Verb("signed up")} {text.User(data.Id("userId")!.Value)} to {EventName(data, text)}{suffix}");
    }

    private static ActivityDescription MessageEdited(ActivityRecord record, string actor, ChangeData data, IActivityText text)
    {
        var where = data.Id("channelId") is { } channelId || data.Text("channelName") != null ? $" in {text.Channel(data.Id("channelId"), data.Text("channelName"))}" : "";
        var jump = data.Id("channelId") is { } channel && record.EntityId is { } messageId ? text.Jump(channel, messageId) : "";
        var now = data.Text("after") is { } after ? $": {text.Quote(after)}" : "";
        var before = data.Text("before") is { } original ? $" (originally {text.Quote(original)})" : "";

        return new("✏️", $"{actor} {text.Verb("edited")} a message{where}{jump}{now}{before}");
    }

    private static ActivityDescription MessageDeleted(ActivityRecord record, ChangeData data, IActivityText text)
    {
        var author = data.Id("authorId") is { } authorId ? $" by {text.User(authorId)}" : "";
        var where = data.Id("channelId") is not null || data.Text("channelName") != null ? $" in {text.Channel(data.Id("channelId"), data.Text("channelName"))}" : "";
        var by = record.UserId is { } deleter && deleter != data.Id("authorId") ? $", by {text.User(deleter)}" : "";
        var preview = data.Text("content") is { } content ? $": {text.Quote(content)}" : "";

        return new("🗑️", $"A message{author} was {text.Verb("deleted")}{where}{by}{preview}");
    }

    private static ActivityDescription Reaction(ActivityRecord record, string actor, ChangeData data, IActivityText text, bool added)
    {
        var emoji = text.Emoji(data.Text("emoji") ?? data.Text("emojiName") ?? "a reaction", data.Text("emojiName"));
        var verb = added ? $"{text.Verb("reacted")} {emoji}" : $"{text.Verb("removed")} their {emoji} reaction";
        var place = data.Text("eventTitle") is { } title
            ? $"{(added ? "on" : "from")} the event {text.Name(title)}"
            : $"{(added ? "to" : "from")} a message in {text.Channel(data.Id("channelId"), data.Text("channelName"))}{(data.Id("channelId") is { } channelId && record.EntityId is { } messageId ? text.Jump(channelId, messageId) : "")}";

        return new(added ? "😀" : "😶", $"{actor} {verb} {place}");
    }

    private static ActivityDescription Voice(string actor, ChangeData data, IActivityText text)
    {
        var kind = data.Text("kind");
        var channel = text.Channel(data.Id("channelId"), data.Text("channelName"));

        // Entries from before the join and leave were recorded separately only carry the names of the two channels
        if (kind == null)
        {
            var before = data.Text("beforeChannel") is { } b && b != "none" ? b : null;
            var after = data.Text("afterChannel") is { } a && a != "none" ? a : null;
            kind = before == null ? "joined" : after == null ? "left" : "moved";
            channel = text.Channel(null, after ?? before);

            if (kind == "moved")
            {
                return new("🔀", $"{actor} {text.Verb("moved")} from {text.Channel(null, before)} to {channel}");
            }
        }

        return kind switch
        {
            "left" => new("🔇", $"{actor} {text.Verb("left")} {channel}"),
            "moved" => new("🔀", $"{actor} {text.Verb("moved")} from {text.Channel(data.Id("fromChannelId"), data.Text("fromChannelName"))} to {channel}"),
            _ => new("🔊", $"{actor} {text.Verb("joined")} {channel}")
        };
    }

    private static string EventName(ChangeData data, IActivityText text)
    {
        return data.Text("eventTitle") is { } title ? text.Name(title) : "an event";
    }

    private static string Member(ActivityRecord record, IActivityText text)
    {
        var id = record.UserId ?? record.EntityId;

        return id is { } found ? text.User(found) : "Someone";
    }

    /// <summary>Whether the person who did it is the person it was done to (they signed themselves up, rather than an admin doing it for them).</summary>
    private static bool IsSelf(ActivityRecord record, ChangeData data)
    {
        return data.Id("userId") is not { } target || target == record.UserId;
    }

    private static string Words(string pascalCase)
    {
        return PascalCaseBoundary().Replace(pascalCase, " ");
    }

    private static string Capitalised(string word)
    {
        return char.ToUpperInvariant(word[0]) + word[1..];
    }

    [GeneratedRegex("(?<=[a-z])(?=[A-Z])")]
    private static partial Regex PascalCaseBoundary();

    /// <summary>The values in an entry's JSON, read leniently: a missing or differently shaped value is simply not there.</summary>
    private sealed class ChangeData
    {
        private readonly JsonElement? _root;

        private ChangeData(JsonElement? root)
        {
            _root = root;
        }

        public static ChangeData Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new ChangeData(null);
            }

            try
            {
                using var document = JsonDocument.Parse(json);

                return new ChangeData(document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null);
            }
            catch (JsonException)
            {
                return new ChangeData(null);
            }
        }

        public bool Has(string name)
        {
            return Find(name) is { ValueKind: not JsonValueKind.Null };
        }

        public ChangeData Child(string name)
        {
            return new ChangeData(Find(name) is { ValueKind: JsonValueKind.Object } child ? child : null);
        }

        public string? Text(string name)
        {
            return Find(name) is { ValueKind: JsonValueKind.String } value && !string.IsNullOrEmpty(value.GetString()) ? value.GetString() : null;
        }

        public ulong? Id(string name)
        {
            return Find(name) switch
            {
                { ValueKind: JsonValueKind.Number } number when number.TryGetUInt64(out var id) => id,
                { ValueKind: JsonValueKind.String } text when ulong.TryParse(text.GetString(), out var id) => id,
                _ => null
            };
        }

        public int? Number(string name)
        {
            return Find(name) is { ValueKind: JsonValueKind.Number } number && number.TryGetInt32(out var value) ? value : null;
        }

        public bool? Bool(string name)
        {
            return Find(name) is { ValueKind: JsonValueKind.True or JsonValueKind.False } value ? value.GetBoolean() : null;
        }

        public DateTime? Time(string name)
        {
            return Find(name) is { ValueKind: JsonValueKind.String } value && value.TryGetDateTime(out var time)
                ? DateTime.SpecifyKind(time, time.Kind == DateTimeKind.Local ? DateTimeKind.Local : DateTimeKind.Utc).ToUniversalTime()
                : null;
        }

        private JsonElement? Find(string name)
        {
            return _root is { } root && root.TryGetProperty(name, out var value) ? value : null;
        }
    }
}
