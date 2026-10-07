using DiscordBot.Application.Models;
using DiscordBot.Domain.Entities;

namespace DiscordBot.Discord.Presentation;

/// <summary>One message of a ping, with the people it mentions (Discord only notifies the people a message is allowed to mention).</summary>
public sealed record PingChunk(string Text, IReadOnlyList<ulong> UserIds);

/// <summary>Writes a ping as one or more Discord messages: a message holds 2000 characters and mentions at most 100 people.</summary>
public static class EventPingMessage
{
    private const int MaxLength = 1900;
    private const int MaxMentions = 100;

    public static IReadOnlyList<PingChunk> Build(Event evt, PingTarget target, string? note, IReadOnlyList<PingRecipient> recipients)
    {
        var header = string.IsNullOrWhiteSpace(note) ? "" : note.Trim() + "\n";
        var lines = target switch
        {
            // The roster itself is an embed (see EventRosterEmbed); the text only has to notify the people in it
            PingTarget.Roster => new List<(string Text, IReadOnlyList<ulong> Users)> { ($"📣 {Mentions(recipients)}", recipients.Select(r => r.UserId).ToList()) },
            PingTarget.Standby => new List<(string Text, IReadOnlyList<ulong> Users)> { ($"⏳ **Standby for {Escape(evt.Title)}:** {Mentions(recipients)}", recipients.Select(r => r.UserId).ToList()) },
            _ => new List<(string Text, IReadOnlyList<ulong> Users)> { ($"{Mentions(recipients)} you haven't responded to **{Escape(evt.Title)}** yet (starts {DiscordTimestamp.Relative(evt.ScheduledTime)}). React to the announcement above to sign up!", recipients.Select(r => r.UserId).ToList()) }
        };

        return Chunk(header, lines);
    }

    /// <summary>
    /// Fills messages line by line, starting a new one when the next line would not fit. A single line that is too long (a ping of
    /// many people who were not asked by group) is split between its mentions instead.
    /// </summary>
    private static IReadOnlyList<PingChunk> Chunk(string header, List<(string Text, IReadOnlyList<ulong> Users)> lines)
    {
        var chunks = new List<PingChunk>();
        var text = new System.Text.StringBuilder(header);
        var users = new List<ulong>();

        void Flush()
        {
            if (text.Length > 0)
            {
                chunks.Add(new PingChunk(text.ToString().TrimEnd(), users.ToList()));
            }

            text.Clear();
            users.Clear();
        }

        foreach (var line in lines)
        {
            if (text.Length > 0 && (text.Length + line.Text.Length + 1 > MaxLength || users.Count + line.Users.Count > MaxMentions))
            {
                Flush();
            }

            if (line.Text.Length > MaxLength || line.Users.Count > MaxMentions)
            {
                Flush();
                chunks.AddRange(SplitMentions(line.Text, line.Users));
                continue;
            }

            text.Append(line.Text).Append('\n');
            users.AddRange(line.Users);
        }

        Flush();

        return chunks;
    }

    private static IEnumerable<PingChunk> SplitMentions(string text, IReadOnlyList<ulong> ids)
    {
        // The line is "<@a> <@b> ... trailing words": the mentions are cut into runs, and the words follow the last run
        var lastMention = ids.Count == 0 ? "" : $"<@{ids[^1]}>";
        var mentionsEnd = ids.Count == 0 ? 0 : text.IndexOf(lastMention, StringComparison.Ordinal) + lastMention.Length;
        var tail = mentionsEnd < text.Length ? text[mentionsEnd..] : "";
        var perMessage = Math.Min(MaxMentions, MaxLength / 25);

        var batches = ids.Chunk(perMessage).ToList();
        for (var i = 0; i < batches.Count; i++)
        {
            var mentions = string.Join(" ", batches[i].Select(id => $"<@{id}>"));
            yield return new PingChunk(i == batches.Count - 1 ? mentions + tail : mentions, batches[i]);
        }
    }

    private static string Mentions(IEnumerable<PingRecipient> recipients)
    {
        return string.Join(" ", recipients.Select(r => $"<@{r.UserId}>"));
    }

    private static string Escape(string title)
    {
        return DiscordActivityText.Escape(title);
    }
}
