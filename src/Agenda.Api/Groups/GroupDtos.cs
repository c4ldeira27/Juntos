using Agenda.Api.Data;
using Agenda.Api.Schedule;

namespace Agenda.Api.Groups;

public sealed record GroupDto(Guid Id, string Name, IReadOnlyList<MemberDto> Members, int MaxMembers);

/// <param name="NeedsReconnect">This person's Google connection expired: their calendars are hidden until they reconnect.</param>
/// <param name="SharingConfigured">Already chose what to share (otherwise the front end opens onboarding for them).</param>
public sealed record MemberDto(
    Guid UserId,
    string Name,
    string? AvatarUrl,
    string Color,
    GroupRole Role,
    bool IsMe,
    bool NeedsReconnect,
    bool SharingConfigured,
    int SharedSources);

public sealed record CreateGroupRequest(string? Name);

public sealed record InviteDto(string Url, DateTimeOffset ExpiresAt);

/// <param name="Status">valid | expired | used | alreadyMember | inAnotherGroup | full</param>
/// <param name="InvitedBy">Null if the inviter no longer exists (the front end shows a localized placeholder).</param>
public sealed record InvitePreviewDto(string GroupName, string? InvitedBy, DateTimeOffset ExpiresAt, string Status);

/// <param name="Visibility">Null = not shared.</param>
public sealed record SharingItemDto(string SourceId, SourceKind Kind, string? Name, bool Primary, SourceVisibility? Visibility);

public sealed record SharingChoice(string SourceId, string Name, SourceVisibility Visibility);

/// <summary>Replaces all of the user's choices in the group (anything not in the list stops being shared).</summary>
public sealed record UpdateSharingRequest(IReadOnlyList<SharingChoice>? Sources);
