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
        groups.MapGet("/current", GetCurrentAsync);
        groups.MapPost("/", CreateAsync);
        groups.MapPost("/{groupId:guid}/invites", CreateInviteAsync);
        groups.MapDelete("/{groupId:guid}/members/me", LeaveAsync);
        groups.MapGet("/{groupId:guid}/sharing", GetSharingAsync);
        groups.MapPut("/{groupId:guid}/sharing", UpdateSharingAsync);
        groups.MapGet("/{groupId:guid}/agenda", GetAgendaAsync);

        var invites = app.MapGroup("/api/invites").RequireAuthorization();
        invites.MapGet("/{token}", PreviewInviteAsync);
        invites.MapPost("/{token}/accept", AcceptInviteAsync);
    }

    // ---------------- Grupo ----------------

    /// <summary>204 quando o usuário ainda não está em nenhum grupo (o front mostra a agenda pessoal).</summary>
    private static async Task<IResult> GetCurrentAsync(ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
    {
        if (principal.GetUserId() is not { } userId) return Results.Unauthorized();

        var groupId = await db.GroupMembers.Where(m => m.UserId == userId).Select(m => (Guid?)m.GroupId).FirstOrDefaultAsync(ct);
        return groupId is { } id ? Results.Ok(await LoadGroupAsync(db, id, userId, ct)) : Results.NoContent();
    }

    private static async Task<IResult> CreateAsync(
        CreateGroupRequest request, ClaimsPrincipal principal, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        if (principal.GetUserId() is not { } userId) return Results.Unauthorized();

        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > GroupRules.MaxNameLength)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["name"] = [$"Informe um nome de 1 a {GroupRules.MaxNameLength} caracteres."],
            });

        if (await db.GroupMembers.AnyAsync(m => m.UserId == userId, ct))
            return Problem(StatusCodes.Status409Conflict, "already_in_group", "Você já participa de um grupo.");

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

    /// <summary>
    /// Sair do grupo. O banco apaga em cascata o que a pessoa compartilhava.
    /// Se era a dona, a pessoa mais antiga assume; se era a última, o grupo deixa de existir.
    /// </summary>
    private static async Task<IResult> LeaveAsync(Guid groupId, ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
    {
        if (principal.GetUserId() is not { } userId) return Results.Unauthorized();

        var member = await db.GroupMembers.SingleOrDefaultAsync(m => m.GroupId == groupId && m.UserId == userId, ct);
        if (member is null) return Results.NotFound();

        // Transação: as três operações acontecem juntas ou nenhuma acontece.
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        db.GroupMembers.Remove(member);
        await db.SaveChangesAsync(ct);

        var remaining = await db.GroupMembers.Where(m => m.GroupId == groupId).OrderBy(m => m.JoinedAt).ToListAsync(ct);
        if (remaining.Count == 0)
        {
            await db.Groups.Where(g => g.Id == groupId).ExecuteDeleteAsync(ct);
        }
        else if (remaining.All(m => m.Role != GroupRole.Owner))
        {
            remaining[0].Role = GroupRole.Owner;
            await db.SaveChangesAsync(ct);
        }

        await tx.CommitAsync(ct);
        return Results.NoContent();
    }

    // ---------------- Convites ----------------

    private static async Task<IResult> CreateInviteAsync(
        Guid groupId, ClaimsPrincipal principal, AppDbContext db, TimeProvider time, HttpContext http, CancellationToken ct)
    {
        if (principal.GetUserId() is not { } userId) return Results.Unauthorized();
        if (!await IsMemberAsync(db, groupId, userId, ct)) return Results.NotFound();

        if (await db.GroupMembers.CountAsync(m => m.GroupId == groupId, ct) >= GroupRules.MaxMembers)
            return Problem(StatusCodes.Status409Conflict, "group_full", $"O grupo já tem {GroupRules.MaxMembers} pessoas.");

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

        // O token em texto puro só existe aqui, na resposta. No banco fica apenas o hash.
        var url = $"{http.Request.Scheme}://{http.Request.Host}/convite/{token}";
        return Results.Ok(new InviteDto(url, invite.ExpiresAt));
    }

    private static async Task<IResult> PreviewInviteAsync(
        string token, ClaimsPrincipal principal, AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        if (principal.GetUserId() is not { } userId) return Results.Unauthorized();

        var invite = await FindInviteAsync(db, token, userId, time.GetUtcNow(), ct);
        if (invite is null) return Problem(StatusCodes.Status404NotFound, "invite_not_found", "Convite não encontrado.");

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
                return Problem(StatusCodes.Status404NotFound, "invite_not_found", "Convite não encontrado.");
            case "alreadyMember": // idempotente: clicar duas vezes no link não é erro
                return Results.Ok(await LoadGroupAsync(db, invite.GroupId, userId, ct));
            case "expired" or "used":
                return Problem(StatusCodes.Status410Gone, "invite_invalid", "Este convite expirou ou já foi usado.");
            case "inAnotherGroup":
                return Problem(StatusCodes.Status409Conflict, "already_in_group", "Você já participa de outro grupo.");
            case "full":
                return Problem(StatusCodes.Status409Conflict, "group_full", "Este grupo já está cheio.");
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // "Reserva" o convite de forma atômica: o UPDATE só afeta a linha se ainda não foi usada.
        // Se duas pessoas aceitarem ao mesmo tempo, só uma recebe 1 linha afetada.
        var claimed = await db.GroupInvites
            .Where(i => i.Id == invite.InviteId && i.UsedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(i => i.UsedAt, now)
                .SetProperty(i => i.UsedByUserId, userId), ct);
        if (claimed == 0)
            return Problem(StatusCodes.Status410Gone, "invite_invalid", "Este convite expirou ou já foi usado.");

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
        await tx.CommitAsync(ct);

        return Results.Ok(await LoadGroupAsync(db, invite.GroupId, userId, ct));
    }

    private sealed record InviteLookup(Guid InviteId, Guid GroupId, string GroupName, string InvitedBy, DateTimeOffset ExpiresAt, string Status);

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

        var inAnotherGroup = !invite.IsMember && await db.GroupMembers.AnyAsync(m => m.UserId == userId, ct);

        // A ordem importa: "já é membro" vence "já foi usado" (o próprio convidado reabrindo o link).
        var status =
            invite.IsMember ? "alreadyMember" :
            invite.UsedAt is not null ? "used" :
            invite.ExpiresAt <= now ? "expired" :
            inAnotherGroup ? "inAnotherGroup" :
            invite.MemberCount >= GroupRules.MaxMembers ? "full" :
            "valid";

        return new InviteLookup(invite.Id, invite.GroupId, invite.GroupName, invite.InvitedBy ?? "Alguém", invite.ExpiresAt, status);
    }

    // ---------------- Compartilhamento ----------------

    /// <summary>Minhas agendas do Google + como cada uma está compartilhada neste grupo.</summary>
    private static async Task<IResult> GetSharingAsync(
        Guid groupId, ClaimsPrincipal principal, AppDbContext db, AgendaService agenda, CancellationToken ct)
    {
        if (principal.GetUserId() is not { } userId) return Results.Unauthorized();
        if (!await IsMemberAsync(db, groupId, userId, ct)) return Results.NotFound();

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

        // Compartilhada mas que sumiu do Google (ex.: deixou de seguir a agenda): mostra para a pessoa poder desmarcar.
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

        var member = await db.GroupMembers.SingleOrDefaultAsync(m => m.GroupId == groupId && m.UserId == userId, ct);
        if (member is null) return Results.NotFound();

        var choices = request.Sources ?? [];
        if (choices.Count > GroupRules.MaxSharedSources)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["sources"] = [$"No máximo {GroupRules.MaxSharedSources} agendas."],
            });

        var rows = new List<SharedSource>();
        foreach (var choice in choices.DistinctBy(c => c.SourceId))
        {
            if (!SourceId.TryParse(choice.SourceId, out var sourceId) || sourceId.GoogleId.Length > 255)
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["sources"] = [$"Fonte inválida: '{choice.SourceId}'."],
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

        // Substituir tudo é mais simples (e menos sujeito a bug) do que calcular o que mudou.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.SharedSources.Where(s => s.GroupId == groupId && s.UserId == userId).ExecuteDeleteAsync(ct);
        db.SharedSources.AddRange(rows);
        member.SharingConfiguredAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Results.NoContent();
    }

    // ---------------- Agenda do grupo ----------------

    private static async Task<IResult> GetAgendaAsync(
        Guid groupId, DateTimeOffset from, DateTimeOffset to,
        ClaimsPrincipal principal, AppDbContext db, AgendaService agenda, CancellationToken ct)
    {
        if (principal.GetUserId() is not { } userId) return Results.Unauthorized();

        var errors = AgendaRange.Validate(from, to);
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        // 404 (e não 403) para quem não é membro: não confirma nem que o grupo existe.
        if (!await IsMemberAsync(db, groupId, userId, ct)) return Results.NotFound();

        return Results.Ok(await agenda.GetGroupAgendaAsync(groupId, userId, from, to, ct));
    }

    // ---------------- Helpers ----------------

    private static Task<bool> IsMemberAsync(AppDbContext db, Guid groupId, Guid userId, CancellationToken ct) =>
        db.GroupMembers.AnyAsync(m => m.GroupId == groupId && m.UserId == userId, ct);

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

        return new GroupDto(
            group.Id,
            group.Name,
            group.Members.Select(m => new MemberDto(
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
                m.SharedSources)).ToList(),
            GroupRules.MaxMembers);
    }

    private static IResult Problem(int status, string code, string title) =>
        Results.Problem(statusCode: status, title: title, extensions: new Dictionary<string, object?> { ["code"] = code });
}
