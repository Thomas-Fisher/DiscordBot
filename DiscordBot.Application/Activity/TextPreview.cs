using System.Text.RegularExpressions;

namespace DiscordBot.Application.Activity;

public static partial class TextPreview
{
    public const int DefaultLength = 100;

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>A one-line preview of some text: white space collapsed, cut with an ellipsis, never in the middle of an emoji. Null when there is nothing to show.</summary>
    public static string? Of(string? text, int maxLength = DefaultLength)
    {
        var flat = text == null ? "" : Whitespace().Replace(text, " ").Trim();
        if (flat.Length == 0)
        {
            return null;
        }

        if (flat.Length <= maxLength)
        {
            return flat;
        }

        var cut = maxLength - 1;
        if (char.IsHighSurrogate(flat[cut - 1]))
        {
            cut--;
        }

        return flat[..cut] + "…";
    }
}
