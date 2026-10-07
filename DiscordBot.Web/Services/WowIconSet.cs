namespace DiscordBot.Web.Services;

public sealed class WowIconSet
{
    public static readonly WowIconSet None = new(null);

    private readonly IReadOnlyDictionary<string, string> _urls;

    public WowIconSet(IReadOnlyDictionary<string, string>? urls)
    {
        _urls = urls ?? new Dictionary<string, string>();
    }

    public string? ForClass(string? wowClass)
    {
        return Find(wowClass);
    }

    public string? ForSpec(string? wowClass, string? spec)
    {
        return wowClass == null || spec == null ? null : Find($"{wowClass}_{spec}");
    }

    public string? ForPerson(string? wowClass, string? spec)
    {
        return ForSpec(wowClass, spec) ?? ForClass(wowClass);
    }

    private string? Find(string? key)
    {
        return key != null && _urls.TryGetValue(key.ToLowerInvariant(), out var url) ? url : null;
    }
}
