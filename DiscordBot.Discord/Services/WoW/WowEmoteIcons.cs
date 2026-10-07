namespace DiscordBot.Discord.Services.WoW;

public static class WowEmoteIcons
{
    private const string Prefix = "wow-icons/";
    private const string Extension = ".png";

    public static IReadOnlyList<string> Names { get; } = typeof(WowEmoteIcons).Assembly.GetManifestResourceNames()
        .Where(name => name.StartsWith(Prefix, StringComparison.Ordinal) && name.EndsWith(Extension, StringComparison.Ordinal))
        .Select(name => name[Prefix.Length..^Extension.Length])
        .Order(StringComparer.Ordinal)
        .ToList();

    public static Stream Open(string name)
    {
        return typeof(WowEmoteIcons).Assembly.GetManifestResourceStream(Prefix + name + Extension)
            ?? throw new ArgumentException($"There is no icon called {name}", nameof(name));
    }
}
