using DiscordBot.Web.Services;
using Microsoft.AspNetCore.Components;

namespace DiscordBot.Web.Pages;

public partial class Settings
{
    [Parameter]
    public string GuildId { get; set; } = null!;

    public class GuildSettingModel
    {
        public string GuildId { get; set; } = null!;
        public string GuildName { get; set; } = null!;
        public string EchoChannelId { get; set; } = "";
        public bool EchoEnabled { get; set; } = true;
        public string TimeZone { get; set; } = "UTC";
        public HashSet<string> DisabledAuditCategories { get; set; } = new();
    }

    private static readonly string[] AuditCategoryNames = { "Members", "Channels", "Roles", "Messages", "Voice", "Reactions" };

    private static void SetAuditCategory(GuildSettingModel setting, string category, bool recorded)
    {
        if (recorded)
        {
            setting.DisabledAuditCategories.Remove(category);
        }
        else
        {
            setting.DisabledAuditCategories.Add(category);
        }
    }

    private GuildSettingModel? CurrentGuildSetting { get; set; }
    private List<string> TimeZones { get; set; } = new();
    private bool IsSaving { get; set; }
    private string? SuccessMessage { get; set; }

    protected override async Task OnSignedInAsync()
    {
        await LoadGuildSettings();
    }

    private async Task SaveAsync()
    {
        var setting = CurrentGuildSetting;
        if (setting == null)
        {
            return;
        }

        ErrorMessage = null;
        SuccessMessage = null;

        // An empty box means "no channel"; the API treats 0 as clearing it
        ulong channelId = 0;
        var typed = setting.EchoChannelId.Trim();
        if (typed.Length > 0 && !ulong.TryParse(typed, out channelId))
        {
            ErrorMessage = "The channel ID must be a number.";
            return;
        }

        IsSaving = true;
        var result = await ApiService.UpdateGuildSettingsAsync(setting.GuildId, channelId, setting.EchoEnabled, setting.TimeZone, setting.DisabledAuditCategories.ToList());
        IsSaving = false;

        if (result.IsSuccess)
        {
            Clock.Invalidate();
            SuccessMessage = "Settings saved.";
        }
        else
        {
            ErrorMessage = $"Could not save the settings. {result.ErrorMessage}";
        }
    }

    private async Task LoadGuildSettings()
    {
        IsLoading = true;
        ErrorMessage = null;
        SuccessMessage = null;

        var guilds = await ApiService.GetGuildsAsync();
        if (!guilds.IsSuccess)
        {
            ErrorMessage = guilds.ErrorMessage;
            IsLoading = false;
            return;
        }

        var guild = guilds.Value!.FirstOrDefault(g => g.Id == GuildId);
        if (guild != null)
        {
            var loaded = await ApiService.GetGuildSettingsAsync(guild.Id);
            if (!loaded.IsSuccess)
            {
                ErrorMessage = loaded.ErrorMessage;
                IsLoading = false;
                return;
            }

            var settings = loaded.Value!;
            CurrentGuildSetting = new GuildSettingModel
            {
                GuildId = guild.Id,
                GuildName = guild.Name,
                EchoChannelId = settings.EchoChannelId is > 0 ? settings.EchoChannelId.Value.ToString() : "",
                EchoEnabled = settings.EchoEnabled,
                TimeZone = settings.TimeZone,
                DisabledAuditCategories = settings.DisabledAuditCategories.ToHashSet()
            };

            var zones = await ApiService.GetTimeZonesAsync();
            TimeZones = zones.IsSuccess ? zones.Value! : new List<string> { settings.TimeZone };
        }

        IsLoading = false;
    }
}
