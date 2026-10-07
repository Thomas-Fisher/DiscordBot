using DiscordBot.Domain.Common;
using DiscordBot.Domain.Enums;

namespace DiscordBot.Application.Errors;

public static class SignupErrors
{
    /// <summary>The event is cancelled, completed or deleted, so it no longer takes signups.</summary>
    public static readonly Error EventNotOpen = new("signup.event_not_open", "This event is not open for signups (it is cancelled or finished).");

    /// <summary>The class/spec is not a known WoW class and spec, or the event is not a WoW event.</summary>
    public static readonly Error InvalidClassOrSpec = new("signup.invalid_class_or_spec", "That is not a valid WoW class and spec for this event.");

    public static readonly Error ClassAndSpecRequiredTogether = new("signup.class_and_spec_together", "Both a class and a spec are required together.");

    /// <summary>A class/spec can only be set on someone who is (or is becoming) signed up.</summary>
    public static readonly Error ClassSpecRequiresSignedUp = new("signup.class_spec_requires_signup", "A class and spec can only be set on someone who is signed up.");

    public static readonly Error InvalidStatus = new("signup.invalid_status", $"Invalid status. Valid values: {string.Join(", ", Enum.GetNames<SignupStatus>())}");

    public static readonly Error NotSignedUp = new("signup.not_signed_up", "That user is not signed up for this event.", ErrorKind.NotFound);

    public static readonly Error AlreadySignedUp = new("signup.already_signed_up", "That user is already signed up for this event.");

    public static readonly Error NotAMember = new("signup.not_a_member", "That person is not a member of this server.");

    public static readonly Error RosterNeedsWowEvent = new("roster.not_a_wow_event", "Raid groups are only used for World of Warcraft events.");

    public static readonly Error RosterBadGroup = new("roster.bad_group", $"A group number must be between 1 and {RosterRules.MaxGroups}.");

    public static readonly Error RosterGroupFull = new("roster.group_full", $"A group holds at most {RosterRules.GroupSize} people.");

    public static readonly Error RosterNotSignedUp = new("roster.not_signed_up", "Only people who are signed up, or unsure, can be put in a group.");

    public static readonly Error NothingToChange = new("signup.nothing_to_change", "No valid fields to update.");
}
