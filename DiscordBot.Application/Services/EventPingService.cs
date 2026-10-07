using DiscordBot.Application.Errors;
using DiscordBot.Application.Interfaces;
using DiscordBot.Application.Models;
using DiscordBot.Domain.Entities;
using DiscordBot.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace DiscordBot.Application.Services;

public class EventPingService : IEventPingService
{
    /// <summary>How long a ping is remembered, so asking for the same one again shortly after can warn about it.</summary>
    private static readonly TimeSpan RecentWindow = TimeSpan.FromMinutes(10);

    private readonly ISignupRepository _signups;
    private readonly IGuildDirectory _directory;
    private readonly IEventPingSender _sender;
    private readonly IActivityLogger _activityLogger;
    private readonly IMemoryCache _cache;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EventPingService> _logger;

    public EventPingService(
        ISignupRepository signups,
        IGuildDirectory directory,
        IEventPingSender sender,
        IActivityLogger activityLogger,
        IMemoryCache cache,
        TimeProvider timeProvider,
        ILogger<EventPingService> logger)
    {
        _signups = signups;
        _directory = directory;
        _sender = sender;
        _activityLogger = activityLogger;
        _cache = cache;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result<PingPreview>> PreviewAsync(Event evt, PingRequest request)
    {
        var recipients = await ResolveAsync(evt, request);
        if (!recipients.Success)
        {
            return Result<PingPreview>.Fail(recipients.Error!);
        }

        var recent = _cache.TryGetValue(CacheKey(evt, request.Target), out DateTime at) ? at : (DateTime?)null;

        return Result<PingPreview>.Ok(new PingPreview(recipients.Value!, recent));
    }

    public async Task<Result<int>> SendAsync(Event evt, PingRequest request, ActivityContext context)
    {
        var note = request.Note?.Trim();
        if (note?.Length > PingErrors.MaxNoteLength)
        {
            return Result<int>.Fail(PingErrors.NoteTooLong);
        }

        var recipients = await ResolveAsync(evt, request);
        if (!recipients.Success)
        {
            return Result<int>.Fail(recipients.Error!);
        }

        if (recipients.Value!.Count == 0)
        {
            return Result<int>.Fail(PingErrors.NoOneToPing);
        }

        try
        {
            await _sender.SendAsync(evt, request.Target, string.IsNullOrEmpty(note) ? null : note, recipients.Value);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not post a {Target} ping for event {EventId}", request.Target, evt.EventId);

            return Result<int>.Fail(PingErrors.SendFailed);
        }

        _cache.Set(CacheKey(evt, request.Target), _timeProvider.GetUtcNow().UtcDateTime, RecentWindow);

        await _activityLogger.LogAsync(new ActivityEntry(ActivityActionType.PingSent, ActivityEntity.Event, evt.EventId, evt.GuildId, context.UserId, context.Origin,
            new { eventTitle = evt.Title, target = request.Target.ToString(), people = recipients.Value.Count }));

        return Result<int>.Ok(recipients.Value.Count);
    }

    private async Task<Result<IReadOnlyList<PingRecipient>>> ResolveAsync(Event evt, PingRequest request)
    {
        if (!evt.CanBeChanged)
        {
            return Result<IReadOnlyList<PingRecipient>>.Fail(PingErrors.EventNotOpen);
        }

        if (evt.ChannelId == null || evt.MessageId == null)
        {
            return Result<IReadOnlyList<PingRecipient>>.Fail(PingErrors.NotAnnounced);
        }

        var signups = await _signups.GetEventSignupsAsync(evt.EventId);
        var signedUp = signups.Where(s => s.Status == SignupStatus.SignedUp).OrderBy(s => s.SignupOrder ?? int.MaxValue).ThenBy(s => s.SignedUpAt).ToList();

        switch (request.Target)
        {
            case PingTarget.Roster:
                // Whoever the organiser put in a group, including people who answered unsure
                return Ok(signups.Where(s => s.GroupNumber.HasValue && s.Status is SignupStatus.SignedUp or SignupStatus.Unsure)
                    .OrderBy(s => s.GroupNumber).ThenBy(s => s.SignupOrder ?? int.MaxValue).ThenBy(s => s.SignedUpAt)
                    .Select(s => new PingRecipient(s.UserId, s.GroupNumber, s.WowClass, s.WowSpec, s.Status == SignupStatus.Unsure)));

            case PingTarget.Standby:
                return Ok(signedUp.Where(s => !s.GroupNumber.HasValue).Select(s => new PingRecipient(s.UserId, null, s.WowClass, s.WowSpec)));

            default:
                return await ResolveNoResponseAsync(evt, request, signups);
        }
    }

    private async Task<Result<IReadOnlyList<PingRecipient>>> ResolveNoResponseAsync(Event evt, PingRequest request, IReadOnlyList<EventSignup> signups)
    {
        var roleIds = (request.RoleIds ?? evt.MentionRoleIds).Distinct().ToList();
        if (roleIds.Count == 0)
        {
            return Result<IReadOnlyList<PingRecipient>>.Fail(PingErrors.NeedsRoles);
        }

        if (await _directory.GetPingableRolesAsync(evt.GuildId) is not { } pingable)
        {
            return Result<IReadOnlyList<PingRecipient>>.Fail(PingErrors.GuildUnavailable);
        }

        if (roleIds.Any(role => pingable.All(p => p.Id != role)))
        {
            return Result<IReadOnlyList<PingRecipient>>.Fail(PingErrors.UnknownRole);
        }

        if (await _directory.GetRoleMemberIdsAsync(evt.GuildId, roleIds) is not { } members)
        {
            return Result<IReadOnlyList<PingRecipient>>.Fail(PingErrors.GuildUnavailable);
        }

        // Answering means signing up, signing off or (unless the organiser wants them nudged too) saying unsure
        var answered = signups.Where(s => !(request.IncludeUnsure && s.Status == SignupStatus.Unsure)).Select(s => s.UserId).ToHashSet();

        return Ok(members.Where(id => !answered.Contains(id)).Distinct().Order().Select(id => new PingRecipient(id, null)));
    }

    private static Result<IReadOnlyList<PingRecipient>> Ok(IEnumerable<PingRecipient> recipients)
    {
        return Result<IReadOnlyList<PingRecipient>>.Ok(recipients.ToList());
    }

    private static string CacheKey(Event evt, PingTarget target)
    {
        return $"ping:{evt.EventId}:{target}";
    }
}
