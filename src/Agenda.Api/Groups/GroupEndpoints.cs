using System.Security.Claims;
using Agenda.Api.Auth;
using Agenda.Api.Data;
using Agenda.Api.GoogleApi;
using Agenda.Api.Schedule;
using Microsoft.EntityFrameworkCore;

namespace Agenda.Api.Groups;

public static class GroupEndpoints
{
    public static void MapGroupEndpoints(this IEndpointRouteBuilder app)
    {
        var groups = app.MapGroup("/api/groups").RequireAuthorization();
        groups.MapGet("/", ListAsync);
        groups.MapPost("/", CreateAsync);
        groups.MapGet("/{groupId:guid}", GetAsync);
        groups.MapPatch("/{groupId:guid}", RenameAsync);
        groups.MapDelete("/{groupId:guid}", DeleteAsync);

        groups.MapDelete("/{groupId:guid}/members/me", LeaveAsync);
        groups.MapDelete("/{groupId:guid}/members/{userId:guid}", RemoveMemberAsync);
        groups.MapPut("/{groupId:guid}/members/{userId:guid}/role", ChangeRoleAsync);

        groups.MapPost("/{groupId:guid}/invites", CreateInviteAsync);
        groups.MapGet("/{groupId:guid}/sharing", GetSharingAsync);
        groups.MapPut("/{groupId:guid}/sharing", UpdateSharingAsync);
        groups.MapGet("/{groupId:guid}/agenda", GetAgendaAsync);

        var invites = app.MapGroup("/api/invites").RequireAuthorization();
        invites.MapGet("/{token}", PreviewInviteAsync);
        invites.MapPost("/{token}/accept", AcceptInviteAsync);
    }

    // ---------------- Groups ----------------

    /// <summary>Every group the user belongs to, for the group switcher (empty list = personal agenda only).</summary>
    private static async Task<IResult> ListAsync(ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
    {
        if (principal.GetUserId() is not { } userId) return Results.Unauthorized();

        var groups = await db.GroupMembers.AsNoTracking()
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.JoinedAt)
            .Select(m => new GroupSummaryDto(m.GroupId, m.Group.Name, m.Role, m.Color, m.Group.Members.Count))
            .ToListAsync(ct);

        return Results.Ok(groups);
    }

    private static async Task<IResult> GetAsync(Guid groupId, ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
    {
        if (principal.GetUserId() is not { } userId) return Results.Unauthorized();
        if (await GetRoleAsync(db, groupId, userId, ct) is null) return Results.NotFound();

        return Results.Ok(await LoadGroupAsync(db, groupId, userId, ct));
    }

    private static async Task<IResult> CreateAsync(
        CreateGroupRequest request, ClaimsPrincipal principal, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        if (principal.GetUserId() is not { } userId) return Results.Unauthorized();
        if (ValidateName(request.Name) is not { } name) return InvalidName();

        if (await db.GroupMembers.CountAsync(m => m.UserId == userId, ct) >= GroupRules.MaxGroupsPerUser)
            return Problem(StatusCodes.Status409Conflict, "too_many_groups", $"You can be in at most {GroupRules.MaxGroupsPerUser} groups.");

        var now = time.GetUtcNow();
        var group = new Group { Id = Guid.CreateVersion7(), Name = name, CreatedByUserId = userId, CreatedAt = now };
        group.Members.Add(new GroupMember
        {
            GroupId = group.Id,
            UserId = userId,
            Role = GroupRole.Owner,
            Color = GroupRules.NextColor([]),
            JoinedAt = now,
        });
        db.Groups.Add(group);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/groups/{group.Id}", await LoadGroupAsync(db, group.Id, userId, ct));
    }

    private static async Task<IResult> RenameAsync(
        Guid groupId, RenameGroupRequest request, ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
    {
        if (principal.GetUserId() is not { } userId) return Results.Unauthorized();
        if (await GetRoleAsync(db, groupId, userId, ct) is not { } role) return Results.NotFound();
        if (!GroupRules.CanManage(role)) return Forbidden();
        if (ValidateName(request.Name) is not { } name) return InvalidName();

        await db.Groups.Where(g => g.Id == groupId).ExecuteUpdateAsync(s => s.SetProperty(g => g.Name, name), ct);
        return Results.Ok(await LoadGroupAsync(db, groupId, userId, ct));
    }

    /// <summary>Owner only. Members, shared calendars and invites go with it (database cascade).</summary>
    private static async Task<IResult> DeleteAsync(Guid groupId, ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
    {
        if (principal.GetUserId() is not { } userId) return Results.Unauthorized();
        if (await GetRoleAsync(db, groupId, userId, ct) is not { } role) return Results.NotFound();
        if (!GroupRules.CanDelete(role)) return Forbidden();

        await db.Groups.Where(g => g.Id == groupId).ExecuteDeleteAsync(ct);
        return Results.NoContent();
    }

    // ---------------- Members ----------------

    /// <summary>
    /// Leave the group. The database cascades the delete of everything the person shared.
    /// If they were the owner, the longest-standing admin (or member) takes over; if they were the last one, the group is deleted.
    /// </summary>
    private static async Task<IResult> LeaveAsync(Guid groupId, ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
    {
        if (principal.GetUserId() is not { } userId) return Results.Unauthorized();

        // Transaction: the three operations happen together or not at all.
        return await InTransactionAsync(db, async () =>
        {
            var member = await db.GroupMembers.SingleOrDefaultAsync(m => m.GroupId == groupId && m.UserId == userId, ct);
            if (member is null) return Results.NotFound();

            db.GroupMembers.Remove(member);
            await db.SaveChangesAsync(ct);

            var remaining = await db.GroupMembers.Where(m => m.GroupId == groupId).ToListAsync(ct);
            if (remaining.Count == 0)
            {
                await db.Groups.Where(g => g.Id == groupId).ExecuteDeleteAsync(ct);
            }
            else if (remaining.All(m => m.Role != GroupRole.Owner))
            {
                GroupRules.NextOwner(remaining)!.Role = GroupRole.Owner;
                await db.SaveChangesAsync(ct);
            }

            return Results.NoContent();
        }, ct);
    }

    private static async Task<IResult> RemoveMemberAsync(
        Guid groupId, Guid userId, ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
    {
        if (principal.GetUserId() is not { } actorId) return Results.Unauthorized();
        if (actorId == userId)
            return Problem(StatusCodes.Status400BadRequest, "use_leave", "To leave the group, use /members/me.");

        var members = await db.GroupMembers
            .Where(m => m.GroupId == groupId && (m.UserId == actorId || m.UserId == userId))
            .ToListAsync(ct);
        var actor = members.SingleOrDefault(m => m.UserId == actorId);
        if (actor is null) return Results.NotFound();
        var target = members.SingleOrDefault(m => m.UserId == userId);
        if (target is null) return Results.NotFound();
        if (!GroupRules.CanRemove(actor.Role, target.Role)) return Forbidden();

        // Their shared calendars go with the membership (cascade): they stop showing for everyone right away.
        db.GroupMembers.Remove(target);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ChangeRoleAsync(
        Guid groupId, Guid userId, ChangeRoleRequest request, ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
    {
        if (principal.GetUserId() is not { } actorId) return Results.Unauthorized();
        if (request.Role is not (GroupRole.Admin or GroupRole.Member))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["role"] = ["Role must be 'admin' or 'member'."] });

        var members = await db.GroupMembers
            .Where(m => m.GroupId == groupId && (m.UserId == actorId || m.UserId == userId))
            .ToListAsync(ct);
        var actor = members.SingleOrDefault(m => m.UserId == actorId);
        if (actor is null) return Results.NotFound();
        if (!GroupRules.CanChangeRoles(actor.Role)) return Forbidden();
        var target = members.SingleOrDefault(m => m.UserId == userId);
        if (target is null) return Results.NotFound();
        // The owner can't demote themselves this way: the group would be left without an owner.
        if (target.Role == GroupRole.Owner) return Forbidden();

        target.Role = request.Role;
        await db.SaveChangesAsync(ct);
        return Results.Ok(await LoadGroupAsync(db, groupId, actorId, ct));
    }

    // ---------------- Invites ----------------

    private static async Task<IResult> CreateInviteAsync(
        Guid groupId, ClaimsPrincipal principal, AppDbContext db, TimeProvider time, HttpContext http, CancellationToken ct)
    {
        if (principal.GetUserId() is not { } userId) return Results.Unauthorized();
        if (await GetRoleAsync(db, groupId, userId, ct) is not { } role) return Results.NotFound();
        if (!GroupRules.CanManage(role)) return Forbidden();

        if (await db.GroupMembers.CountAsync(m => m.GroupId == groupId, ct) >= GroupRules.MaxMembers)
            return Problem(StatusCodes.Status409Conflict, "group_full", $"The group already has {GroupRules.MaxMembers} people.");

        var now = time.GetUtcNow();
        var (token, hash) = InviteTokens.Create();
        var invite = new GroupInvite
        {
            Id = Guid.CreateVersion7(),
            GroupId = groupId,
            TokenHash = hash,
            CreatedByUserId = userId,
            CreatedAt = now,
            ExpiresAt = now + GroupRules.InviteLifetime,
        };
        db.GroupInvites.Add(invite);
        await db.SaveChangesAsync(ct);

        // The plain-text token only exists here, in the response. The database keeps only the hash.
        var url = $"{http.Request.Scheme}://{http.Request.Host}/invite/{token}";
        return Results.Ok(new InviteDto(url, invite.ExpiresAt));
    }

    private static async Task<IResult> PreviewInviteAsync(
        string token, ClaimsPrincipal principal, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        if (principal.GetUserId() is not { } userId) return Results.Unauthorized();

        var invite = await FindInviteAsync(db, token, userId, time.GetUtcNow(), ct);
        if (invite is null) return Problem(StatusCodes.Status404NotFound, "invite_not_found", "Invite not found.");

        return Results.Ok(new InvitePreviewDto(invite.GroupName, invite.InvitedBy, invite.ExpiresAt, invite.Status));
    }

    private static async Task<IResult> AcceptInviteAsync(
        string token, ClaimsPrincipal principal, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        if (principal.GetUserId() is not { } userId) return Results.Unauthorized();

        var now = time.GetUtcNow();
        var invite = await FindInviteAsync(db, token, userId, now, ct);

        switch (invite?.Status)
        {
            case null:
                return Problem(StatusCodes.Status404NotFound, "invite_not_found", "Invite not found.");
            case "alreadyMember": // idempotent: clicking the link twice isn't an error
                return Results.Ok(await LoadGroupAsync(db, invite.GroupId, userId, ct));
            case "expired" or "used":
                return Problem(StatusCodes.Status410Gone, "invite_invalid", "This invite has expired or was already used.");
            case "tooManyGroups":
                return Problem(StatusCodes.Status409Conflict, "too_many_groups", $"You can be in at most {GroupRules.MaxGroupsPerUser} groups.");
            case "full":
                return Problem(StatusCodes.Status409Conflict, "group_full", "This group is full.");
        }

        var joined = await InTransactionAsync(db, async () =>
        {
            // "Claims" the invite atomically: the UPDATE only affects the row if it hasn't been used yet.
            // If two people accept at the same time, only one gets 1 affected row.
            var claimed = await db.GroupInvites
                .Where(i => i.Id == invite.InviteId && i.UsedAt == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(i => i.UsedAt, now)
                    .SetProperty(i => i.UsedByUserId, userId), ct);
            if (claimed == 0) return false;

            var usedColors = await db.GroupMembers.Where(m => m.GroupId == invite.GroupId).Select(m => m.Color).ToListAsync(ct);
            db.GroupMembers.Add(new GroupMember
            {
                GroupId = invite.GroupId,
                UserId = userId,
                Role = GroupRole.Member,
                Color = GroupRules.NextColor(usedColors),
                JoinedAt = now,
            });
            await db.SaveChangesAsync(ct);
            return true;
        }, ct);

        return joined
            ? Results.Ok(await LoadGroupAsync(db, invite.GroupId, userId, ct))
            : Problem(StatusCodes.Status410Gone, "invite_invalid", "This invite has expired or was already used.");
    }

    private sealed record InviteLookup(Guid InviteId, Guid GroupId, string GroupName, string? InvitedBy, DateTimeOffset ExpiresAt, string Status);

    private static async Task<InviteLookup?> FindInviteAsync(AppDbContext db, string token, Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        var hash = InviteTokens.Hash(token);
        var invite = await db.GroupInvites.AsNoTracking()
            .Where(i => i.TokenHash == hash)
            .Select(i => new
            {
                i.Id,
                i.GroupId,
                i.ExpiresAt,
                i.UsedAt,
                GroupName = i.Group.Name,
                InvitedBy = db.Users.Where(u => u.Id == i.CreatedByUserId).Select(u => u.Name).FirstOrDefault(),
                MemberCount = i.Group.Members.Count,
                IsMember = i.Group.Members.Any(m => m.UserId == userId),
            })
            .SingleOrDefaultAsync(ct);
        if (invite is null) return null;

        var tooManyGroups = !invite.IsMember
                            && await db.GroupMembers.CountAsync(m => m.UserId == userId, ct) >= GroupRules.MaxGroupsPerUser;

        // Order matters: "already a member" wins over "already used" (the invitee reopening the link).
        var status =
            invite.IsMember ? "alreadyMember" :
            invite.UsedAt is not null ? "used" :
            invite.ExpiresAt <= now ? "expired" :
            tooManyGroups ? "tooManyGroups" :
            invite.MemberCount >= GroupRules.MaxMembers ? "full" :
            "valid";

        return new InviteLookup(invite.Id, invite.GroupId, invite.GroupName, invite.InvitedBy, invite.ExpiresAt, status);
    }

    // ---------------- Sharing (per group) ----------------

    /// <summary>My Google calendars + how each one is shared in THIS group (each group has its own choices).</summary>
    private static async Task<IResult> GetSharingAsync(
        Guid groupId, ClaimsPrincipal principal, AppDbContext db, AgendaService agenda, CancellationToken ct)
    {
        if (principal.GetUserId() is not { } userId) return Results.Unauthorized();
        if (await GetRoleAsync(db, groupId, userId, ct) is null) return Results.NotFound();

        var connection = await db.GoogleConnections.AsNoTracking().SingleOrDefaultAsync(c => c.UserId == userId, ct);
        var sources = await agenda.GetSourcesAsync(userId, connection?.HasScope(GoogleScopes.TasksReadonly) == true, ct);

        var shares = await db.SharedSources.AsNoTracking()
            .Where(s => s.GroupId == groupId && s.UserId == userId)
            .ToListAsync(ct);
        var shared = shares.ToDictionary(s => new SourceId(s.Kind, s.GoogleId).ToString());

        var items = sources
            .Select(s => new SharingItemDto(s.Id, s.Kind, s.Name, s.Primary,
                shared.TryGetValue(s.Id, out var share) ? share.Visibility : null))
            .ToList();

        // Shared but gone from Google (e.g. unsubscribed from the calendar): listed so the person can uncheck it.
        var known = sources.Select(s => s.Id).ToHashSet();
        items.AddRange(shares
            .Where(s => !known.Contains(new SourceId(s.Kind, s.GoogleId).ToString()))
            .Select(s => new SharingItemDto(new SourceId(s.Kind, s.GoogleId).ToString(), s.Kind, s.DisplayName, false, s.Visibility)));

        return Results.Ok(items);
    }

    private static async Task<IResult> UpdateSharingAsync(
        Guid groupId, UpdateSharingRequest request, ClaimsPrincipal principal, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        if (principal.GetUserId() is not { } userId) return Results.Unauthorized();
        if (await GetRoleAsync(db, groupId, userId, ct) is null) return Results.NotFound();

        var choices = request.Sources ?? [];
        if (choices.Count > GroupRules.MaxSharedSources)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["sources"] = [$"At most {GroupRules.MaxSharedSources} calendars."],
            });

        var rows = new List<SharedSource>();
        foreach (var choice in choices.DistinctBy(c => c.SourceId))
        {
            if (!SourceId.TryParse(choice.SourceId, out var sourceId) || sourceId.GoogleId.Length > 255)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["sources"] = [$"Invalid source: '{choice.SourceId}'."],
                });

            var name = string.IsNullOrWhiteSpace(choice.Name) ? sourceId.GoogleId : choice.Name.Trim();
            rows.Add(new SharedSource
            {
                Id = Guid.CreateVersion7(),
                GroupId = groupId,
                UserId = userId,
                Kind = sourceId.Kind,
                GoogleId = sourceId.GoogleId,
                DisplayName = name.Length > 200 ? name[..200] : name,
                Visibility = choice.Visibility,
            });
        }

        // Replacing everything is simpler (and less bug-prone) than computing what changed.
        return await InTransactionAsync(db, async () =>
        {
            var member = await db.GroupMembers.SingleOrDefaultAsync(m => m.GroupId == groupId && m.UserId == userId, ct);
            if (member is null) return Results.NotFound(); // left the group in the meantime

            await db.SharedSources.Where(s => s.GroupId == groupId && s.UserId == userId).ExecuteDeleteAsync(ct);
            db.SharedSources.AddRange(rows);
            member.SharingConfiguredAt = time.GetUtcNow();
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }, ct);
    }

    // ---------------- Group agenda ----------------

    private static async Task<IResult> GetAgendaAsync(
        Guid groupId, DateTimeOffset from, DateTimeOffset to,
        ClaimsPrincipal principal, AppDbContext db, AgendaService agenda, CancellationToken ct)
    {
        if (principal.GetUserId() is not { } userId) return Results.Unauthorized();

        var errors = AgendaRange.Validate(from, to);
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        // 404 (not 403) for non-members: doesn't even confirm the group exists.
        if (await GetRoleAsync(db, groupId, userId, ct) is null) return Results.NotFound();

        return Results.Ok(await agenda.GetGroupAgendaAsync(groupId, userId, from, to, ct));
    }

    // ---------------- Helpers ----------------

    /// <summary>
    /// The caller's role in the group, or null if they aren't a member. Non-members always get 404
    /// (it doesn't even confirm the group exists); members without permission get 403.
    /// </summary>
    private static Task<GroupRole?> GetRoleAsync(AppDbContext db, Guid groupId, Guid userId, CancellationToken ct) =>
        db.GroupMembers
            .Where(m => m.GroupId == groupId && m.UserId == userId)
            .Select(m => (GroupRole?)m.Role)
            .FirstOrDefaultAsync(ct);

    private static string? ValidateName(string? name)
    {
        var trimmed = name?.Trim();
        return string.IsNullOrEmpty(trimmed) || trimmed.Length > GroupRules.MaxNameLength ? null : trimmed;
    }

    private static IResult InvalidName() =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["name"] = [$"Name must be 1 to {GroupRules.MaxNameLength} characters."],
        });

    /// <summary>
    /// Runs <paramref name="work"/> in a transaction inside EF's execution strategy.
    /// With EnableRetryOnFailure, EF refuses manual transactions outside the strategy: if the connection drops
    /// mid-way it must retry the WHOLE unit, not just the last statement. Each attempt starts with a clean
    /// change tracker so leftovers from a failed attempt (entities marked Added/Deleted) aren't replayed.
    /// </summary>
    private static Task<T> InTransactionAsync<T>(AppDbContext db, Func<Task<T>> work, CancellationToken ct) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var result = await work();
            await tx.CommitAsync(ct);
            return result;
        });

    private static async Task<GroupDto> LoadGroupAsync(AppDbContext db, Guid groupId, Guid viewerId, CancellationToken ct)
    {
        var group = await db.Groups.AsNoTracking()
            .Where(g => g.Id == groupId)
            .Select(g => new
            {
                g.Id,
                g.Name,
                Members = g.Members
                    .OrderBy(m => m.JoinedAt)
                    .Select(m => new
                    {
                        m.UserId,
                        m.User.Name,
                        m.User.AvatarUrl,
                        m.Color,
                        m.Role,
                        m.SharingConfiguredAt,
                        Connection = m.User.GoogleConnection,
                        SharedSources = m.SharedSources.Count,
                    })
                    .ToList(),
            })
            .SingleAsync(ct);

        var members = group.Members.Select(m => new MemberDto(
            m.UserId,
            m.Name,
            m.AvatarUrl,
            m.Color,
            m.Role,
            IsMe: m.UserId == viewerId,
            NeedsReconnect: m.Connection is null
                            || m.Connection.Status != GoogleConnectionStatus.Ok
                            || !m.Connection.HasScope(GoogleScopes.CalendarReadonly),
            SharingConfigured: m.SharingConfiguredAt is not null,
            m.SharedSources)).ToList();

        return new GroupDto(group.Id, group.Name, members.Single(m => m.IsMe).Role, members, GroupRules.MaxMembers);
    }

    private static IResult Forbidden() =>
        Problem(StatusCodes.Status403Forbidden, "forbidden", "Your role in this group doesn't allow this action.");

    private static IResult Problem(int status, string code, string title) =>
        Results.Problem(statusCode: status, title: title, extensions: new Dictionary<string, object?> { ["code"] = code });
}
