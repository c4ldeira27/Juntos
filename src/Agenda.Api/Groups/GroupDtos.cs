using Agenda.Api.Data;
using Agenda.Api.Schedule;

namespace Agenda.Api.Groups;

/// <summary>One line of the group switcher.</summary>
public sealed record GroupSummaryDto(Guid Id, string Name, GroupRole MyRole, string MyColor, int MemberCount);

public sealed record GroupDto(Guid Id, string Name, GroupRole MyRole, IReadOnlyList<MemberDto> Members, int MaxMembers);

/// <param name="NeedsReconnect">This person's Google connection expired: their calendars are hidden until they reconnect.</param>
/// <param name="SharingConfigured">Already chose what to share in this group (otherwise the front end opens onboarding for them).</param>
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

public sealed record RenameGroupRequest(string? Name);

/// <param name="Role">Only Admin or Member: ownership isn't handed out through this endpoint.</param>
public sealed record ChangeRoleRequest(GroupRole Role);

public sealed record InviteDto(string Url, DateTimeOffset ExpiresAt);

/// <param name="Status">valid | expired | used | alreadyMember | tooManyGroups | full</param>
/// <param name="InvitedBy">Null if the inviter no longer exists (the front end shows a localized placeholder).</param>
public sealed record InvitePreviewDto(string GroupName, string? InvitedBy, DateTimeOffset ExpiresAt, string Status);

/// <param name="Visibility">Null = not shared.</param>
public sealed record SharingItemDto(string SourceId, SourceKind Kind, string? Name, bool Primary, SourceVisibility? Visibility);

public sealed record SharingChoice(string SourceId, string Name, SourceVisibility Visibility);

/// <summary>Replaces all of the user's choices in the group (anything not in the list stops being shared).</summary>
public sealed record UpdateSharingRequest(IReadOnlyList<SharingChoice>? Sources);
