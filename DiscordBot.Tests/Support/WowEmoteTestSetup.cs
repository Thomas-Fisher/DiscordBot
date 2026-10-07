using System.Runtime.CompilerServices;
using DiscordBot.Discord.Services.WoW;
using DiscordBot.Domain.Wow;

namespace DiscordBot.Tests.Support;

public static class WowEmoteTestSetup
{
    [ModuleInitializer]
    public static void RegisterEmotes()
    {
        var ids = new Dictionary<string, ulong>();
        var next = 1000UL;

        foreach (var className in WowCatalog.Classes)
        {
            ids[className] = next++;

            foreach (var spec in WowCatalog.SpecsFor(className))
            {
                ids[$"{className}_{spec}"] = next++;
            }
        }

        WowEmotes.Register(ids);
    }
}
