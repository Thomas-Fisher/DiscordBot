using DiscordBot.Domain.Entities;
using DiscordBot.Tests.Support;
using DiscordBot.Discord.Presentation;

namespace DiscordBot.Tests.Unit.Discord;

public class EventListEmbedTests
{
    // Discord rejects an embed whose text adds up to more than this
    private const int DiscordEmbedLimit = 6000;

    private static Event LongEvent(ulong id)
    {
        var evt = TestEvents.Scheduled(id);
        evt.Title = new string('*', 100);         // every character needs escaping
        evt.Description = new string('_', 200);
        evt.DurationMinutes = 90;
        return evt;
    }

    private static int TextLength(global::Discord.Embed embed)
    {
        return (embed.Title?.Length ?? 0) + (embed.Footer?.Text.Length ?? 0) + embed.Fields.Sum(f => f.Name.Length + f.Value.Length);
    }

    [Fact]
    public void Build_ManyLongEventsFullOfMarkdown_StaysWithinDiscordsLimits()
    {
        var events = Enumerable.Range(1, 30).Select(i => LongEvent((ulong)i)).ToList();

        var embed = EventListEmbed.Build(events);

        Assert.Equal(EventListEmbed.MaxEvents, embed.Fields.Length);
        Assert.True(TextLength(embed) < DiscordEmbedLimit, $"embed text was {TextLength(embed)} characters");
        Assert.All(embed.Fields, f => Assert.True(f.Name.Length <= 256 && f.Value.Length <= 1024));
        Assert.Contains("30", embed.Footer!.Value.Text);
    }

    [Fact]
    public void Build_FewEvents_ListsEachWithItsIdAndNoFooter()
    {
        var events = new List<Event> { TestEvents.Scheduled(1), TestEvents.Scheduled(2) };

        var embed = EventListEmbed.Build(events);

        Assert.Equal(2, embed.Fields.Length);
        Assert.Contains("`1`", embed.Fields[0].Value);
        Assert.Null(embed.Footer);
    }

    [Fact]
    public void Build_TitleWithMarkdownAndMentions_IsEscapedSoItCannotBreakTheLayout()
    {
        var evt = TestEvents.Scheduled(1);
        evt.Title = "**bold** @everyone";

        var embed = EventListEmbed.Build(new List<Event> { evt });

        Assert.DoesNotContain("**bold**", embed.Fields[0].Name);
    }
}
