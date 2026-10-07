using Discord;
using DiscordBot.Application.Activity;
using DiscordBot.Domain.Entities;

namespace DiscordBot.Discord.Presentation;

/// <summary>The embed shown by /listevents. Kept within Discord's embed limits however many events there are and whatever they contain.</summary>
public static class EventListEmbed
{
    public const int MaxEvents = 10;

    private const int MaxDescriptionLength = 100;

    public static Embed Build(IReadOnlyList<Event> events)
    {
        var builder = new EmbedBuilder()
            .WithTitle("📅 Scheduled Events")
            .WithColor(Color.Blue);

        foreach (var evt in events.Take(MaxEvents))
        {
            builder.AddField(Format.Sanitize(evt.Title), FieldText(evt));
        }

        if (events.Count > MaxEvents)
        {
            builder.WithFooter($"Showing the next {MaxEvents} of {events.Count} events");
        }

        return builder.Build();
    }

    private static string FieldText(Event evt)
    {
        var duration = evt.DurationMinutes is { } minutes ? $" ({DurationText.Format(minutes)})" : "";
        var lines = new List<string>();

        if (!string.IsNullOrWhiteSpace(evt.Description))
        {
            var description = TextPreview.Of(evt.Description, MaxDescriptionLength)!;
            lines.Add(Format.Sanitize(description));
        }

        lines.Add($"🕐 {DiscordTimestamp.LongDate(evt.ScheduledTime)} {DiscordTimestamp.ShortTime(evt.ScheduledTime)}{duration}");
        lines.Add($"🆔 `{evt.EventId}`");

        return string.Join("\n", lines);
    }
}
