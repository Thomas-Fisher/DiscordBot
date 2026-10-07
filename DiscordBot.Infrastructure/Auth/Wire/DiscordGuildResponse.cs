using System.Text.Json.Serialization;

namespace DiscordBot.Infrastructure.Auth.Wire;

internal sealed class DiscordGuildResponse
{
    public string? Id { get; set; }

    public string? Name { get; set; }

    public string? Icon { get; set; }

    public bool Owner { get; set; }

    [JsonConverter(typeof(PermissionsConverter))]
    public long Permissions { get; set; }
}
