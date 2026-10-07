using DiscordBot.Domain.Enums;

namespace DiscordBot.Domain.Wow;

/// <summary>
/// The World of Warcraft classes and specs that events accept signups for. Names are lower case, matching how they are
/// stored and how the Discord emotes are named.
/// </summary>
public static class WowCatalog
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> SpecsByClass =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["deathknight"] = new[] { "blood", "frost", "unholy" },
            ["druid"] = new[] { "balance", "feral", "guardian", "restoration" },
            ["hunter"] = new[] { "beastmastery", "marksman", "survival" },
            ["mage"] = new[] { "arcane", "fire", "frost" },
            ["monk"] = new[] { "brewmaster", "mistweaver", "windwalker" },
            ["paladin"] = new[] { "holy", "protection", "retribution" },
            ["priest"] = new[] { "discipline", "holy", "shadow" },
            ["rogue"] = new[] { "assassination", "combat", "subtlety" },
            ["shaman"] = new[] { "elemental", "enhancement", "restoration" },
            ["warlock"] = new[] { "affliction", "demonology", "destruction" },
            ["warrior"] = new[] { "arms", "fury", "protection" }
        };

    private static readonly IReadOnlyDictionary<string, WowRole> RolesBySpec =
        new Dictionary<string, WowRole>(StringComparer.OrdinalIgnoreCase)
        {
            ["deathknight_blood"] = WowRole.Tank,
            ["deathknight_frost"] = WowRole.Melee,
            ["deathknight_unholy"] = WowRole.Melee,
            ["druid_balance"] = WowRole.Ranged,
            ["druid_feral"] = WowRole.Melee,
            ["druid_guardian"] = WowRole.Tank,
            ["druid_restoration"] = WowRole.Healer,
            ["hunter_beastmastery"] = WowRole.Ranged,
            ["hunter_marksman"] = WowRole.Ranged,
            ["hunter_survival"] = WowRole.Melee,
            ["mage_arcane"] = WowRole.Ranged,
            ["mage_fire"] = WowRole.Ranged,
            ["mage_frost"] = WowRole.Ranged,
            ["monk_brewmaster"] = WowRole.Tank,
            ["monk_mistweaver"] = WowRole.Healer,
            ["monk_windwalker"] = WowRole.Melee,
            ["paladin_holy"] = WowRole.Healer,
            ["paladin_protection"] = WowRole.Tank,
            ["paladin_retribution"] = WowRole.Melee,
            ["priest_discipline"] = WowRole.Healer,
            ["priest_holy"] = WowRole.Healer,
            ["priest_shadow"] = WowRole.Ranged,
            ["rogue_assassination"] = WowRole.Melee,
            ["rogue_combat"] = WowRole.Melee,
            ["rogue_subtlety"] = WowRole.Melee,
            ["shaman_elemental"] = WowRole.Ranged,
            ["shaman_enhancement"] = WowRole.Melee,
            ["shaman_restoration"] = WowRole.Healer,
            ["warlock_affliction"] = WowRole.Ranged,
            ["warlock_demonology"] = WowRole.Ranged,
            ["warlock_destruction"] = WowRole.Ranged,
            ["warrior_arms"] = WowRole.Melee,
            ["warrior_fury"] = WowRole.Melee,
            ["warrior_protection"] = WowRole.Tank
        };

    // Death Knight and Monk came after the Forever realms' era
    private static readonly HashSet<string> MissingFromForever = new(StringComparer.OrdinalIgnoreCase) { "deathknight", "monk" };

    /// <summary>Every class there is, for recognising emotes. Use <c>ClassesFor</c> to decide what an event offers.</summary>
    public static IReadOnlyCollection<string> Classes => SpecsByClass.Keys.ToList();

    public static IReadOnlyList<string> ClassesFor(EventType type)
    {
        return SpecsByClass.Keys.Where(c => Offers(type, c)).ToList();
    }

    /// <summary>The specs of a class for an event of this type; empty when the type does not offer the class.</summary>
    public static IReadOnlyList<string> SpecsFor(EventType type, string? className)
    {
        return className != null && Offers(type, className) ? SpecsFor(className) : Array.Empty<string>();
    }

    private static bool Offers(EventType type, string className)
    {
        return type switch
        {
            EventType.WowForever => !MissingFromForever.Contains(className),
            EventType.WowClassic => true,
            _ => false
        };
    }

    /// <summary>The specs of a class, or an empty list when the class is unknown.</summary>
    public static IReadOnlyList<string> SpecsFor(string? className)
    {
        return className != null && SpecsByClass.TryGetValue(className, out var specs) ? specs : Array.Empty<string>();
    }

    /// <summary>The role a spec fills in a raid, or null when the class or spec is missing or unknown.</summary>
    public static WowRole? RoleOf(string? className, string? spec)
    {
        return RolesBySpec.TryGetValue($"{className}_{spec}", out var role) ? role : null;
    }

    public static string DisplayName(string className)
    {
        return className.Equals("deathknight", StringComparison.OrdinalIgnoreCase)
            ? "Death Knight"
            : char.ToUpperInvariant(className[0]) + className[1..].ToLowerInvariant();
    }

    /// <summary>
    /// Checks that the spec belongs to the class (case-insensitive) and returns both in their stored, lower-case form.
    /// </summary>
    public static bool TryNormalize(EventType type, string? className, string? spec, out string normalizedClass, out string normalizedSpec)
    {
        normalizedClass = string.Empty;
        normalizedSpec = string.Empty;

        if (string.IsNullOrWhiteSpace(className) || string.IsNullOrWhiteSpace(spec))
        {
            return false;
        }

        var match = SpecsFor(type, className.Trim()).FirstOrDefault(s => s.Equals(spec.Trim(), StringComparison.OrdinalIgnoreCase));
        if (match == null)
        {
            return false;
        }

        normalizedClass = className.Trim().ToLowerInvariant();
        normalizedSpec = match;
        return true;
    }
}
