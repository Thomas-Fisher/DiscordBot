namespace DiscordBot.Domain.Enums;

/// <summary>A kind of server activity the bot records from Discord. A guild can switch a category off.</summary>
public enum AuditCategory
{
    Members,
    Channels,
    Roles,
    Messages,
    Voice,
    Reactions
}

public static class AuditCategories
{
    /// <summary>The category an action belongs to, or null for actions the bot always records (events and signups).</summary>
    public static AuditCategory? Of(ActivityActionType action)
    {
        return action switch
        {
            ActivityActionType.UserJoined or ActivityActionType.UserLeft or ActivityActionType.UserKicked or ActivityActionType.UserBanned => AuditCategory.Members,
            ActivityActionType.ChannelCreated or ActivityActionType.ChannelDeleted => AuditCategory.Channels,
            ActivityActionType.RoleCreated or ActivityActionType.RoleDeleted => AuditCategory.Roles,
            ActivityActionType.MessageCreated or ActivityActionType.MessageDeleted or ActivityActionType.MessageUpdated => AuditCategory.Messages,
            ActivityActionType.VoiceStateChanged => AuditCategory.Voice,
            ActivityActionType.ReactionAdded or ActivityActionType.ReactionRemoved => AuditCategory.Reactions,
            _ => null
        };
    }
}
