using Agenda.Api.Data;
using Agenda.Api.Schedule;

namespace Agenda.Api.Groups;

public sealed record GroupDto(Guid Id, string Name, IReadOnlyList<MemberDto> Members, int MaxMembers);

/// <param name="NeedsReconnect">A conexão Google dessa pessoa expirou: a agenda dela não aparece até reconectar.</param>
/// <param name="SharingConfigured">Já escolheu o que compartilhar (senão o front abre o onboarding para ela).</param>
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
public sealed record InvitePreviewDto(string GroupName, string InvitedBy, DateTimeOffset ExpiresAt, string Status);

/// <param name="Visibility">Nulo = não compartilhada.</param>
public sealed record SharingItemDto(string SourceId, SourceKind Kind, string Name, bool Primary, SourceVisibility? Visibility);

public sealed record SharingChoice(string SourceId, string Name, SourceVisibility Visibility);

/// <summary>Substitui todas as escolhas do usuário no grupo (o que não vier na lista deixa de ser compartilhado).</summary>
public sealed record UpdateSharingRequest(IReadOnlyList<SharingChoice>? Sources);
