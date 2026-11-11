using System.Security.Claims;
using Agenda.Api.Auth;
using Agenda.Api.Data;
using Agenda.Api.GoogleApi;
using Microsoft.EntityFrameworkCore;

namespace Agenda.Api.Schedule;

public sealed record MeResponse(Guid Id, string Name, string Email, string? AvatarUrl, GoogleStatusDto Google);

/// <param name="Status">notConnected | needsReconnect | missingCalendarScope | connected</param>
public sealed record GoogleStatusDto(string Status, bool CanReadTasks);

public static class MeEndpoints
{
    private const int MaxSources = 30;

    public static void MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/me").RequireAuthorization();

        group.MapGet("/", async (ClaimsPrincipal principal, AppDbContext db, CancellationToken ct) =>
        {
            if (principal.GetUserId() is not { } userId) return Results.Unauthorized();

            var user = await db.Users.AsNoTracking()
                .Include(u => u.GoogleConnection)
                .SingleOrDefaultAsync(u => u.Id == userId, ct);

            // A valid cookie for a user that no longer exists (e.g. database recreated in dev).
            if (user is null) return Results.Unauthorized();

            return Results.Ok(new MeResponse(user.Id, user.Name, user.Email, user.AvatarUrl, ToStatus(user.GoogleConnection)));
        });

        group.MapGet("/sources", async (ClaimsPrincipal principal, AppDbContext db, AgendaService agenda, CancellationToken ct) =>
        {
            if (principal.GetUserId() is not { } userId) return Results.Unauthorized();

            var connection = await db.GoogleConnections.AsNoTracking().SingleOrDefaultAsync(c => c.UserId == userId, ct);
            var canReadTasks = connection?.HasScope(GoogleScopes.TasksReadonly) == true;

            return Results.Ok(await agenda.GetSourcesAsync(userId, canReadTasks, ct));
        });

        // e.g. /api/me/agenda?from=2026-11-09T03:00:00Z&to=2026-11-16T03:00:00Z&source=cal:primary&source=tasks:abc
        group.MapGet("/agenda", async (
            ClaimsPrincipal principal,
            DateTimeOffset from,
            DateTimeOffset to,
            string[]? source,
            AgendaService agenda,
            CancellationToken ct) =>
        {
            if (principal.GetUserId() is not { } userId) return Results.Unauthorized();

            var errors = AgendaRange.Validate(from, to);
            if (source is { Length: > MaxSources }) errors["source"] = [$"At most {MaxSources} sources per request."];

            var parsed = new List<SourceId>();
            foreach (var raw in source ?? [])
            {
                if (SourceId.TryParse(raw, out var id)) parsed.Add(id);
                else errors["source"] = [$"Invalid source: '{raw}'. Use 'cal:{{id}}' or 'tasks:{{id}}'."];
            }

            if (errors.Count > 0) return Results.ValidationProblem(errors);
            if (parsed.Count == 0) return Results.Ok(new AgendaResponse([], []));

            return Results.Ok(await agenda.GetOwnAgendaAsync(userId, from, to, parsed, ct));
        });
    }

    private static GoogleStatusDto ToStatus(GoogleConnection? connection) => connection switch
    {
        null => new("notConnected", false),
        { Status: GoogleConnectionStatus.NeedsReconnect } => new("needsReconnect", false),
        _ when !connection.HasScope(GoogleScopes.CalendarReadonly) => new("missingCalendarScope", false),
        _ => new("connected", connection.HasScope(GoogleScopes.TasksReadonly)),
    };
}
