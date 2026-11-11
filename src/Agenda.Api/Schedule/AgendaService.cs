using System.Globalization;
using System.Net;
using Agenda.Api.Data;
using Agenda.Api.GoogleApi;
using Google;
using Google.Apis.Calendar.v3.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using GoogleTask = Google.Apis.Tasks.v1.Data.Task;

namespace Agenda.Api.Schedule;

/// <summary>
/// Reads events and tasks straight from Google (nothing is stored in the database) with a short cache.
/// Why not sync into the database yet? With few users, reading on demand is simpler,
/// stores less personal data and is never stale. Syncing (syncToken + webhooks) is for scale.
/// </summary>
public sealed class AgendaService(
    GoogleClientFactory clientFactory,
    GoogleTokenService tokens,
    AppDbContext db,
    IMemoryCache cache,
    ILogger<AgendaService> logger)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(2);

    private const string EventFields =
        "nextPageToken,items(id,status,summary,location,start,end,visibility,transparency,eventType,attendees(self,responseStatus))";

    private const string TaskFields = "nextPageToken,items(id,title,due,status,deleted,hidden)";

    /// <summary>A source to read: whose it is, which calendar, and how much detail it was shared with.</summary>
    private sealed record SourceRequest(Guid OwnerId, SourceId Source, SourceVisibility Visibility);

    /// <summary>Raw Google response. THIS is what goes into the cache, not the filtered result.</summary>
    private sealed record RawSource(IReadOnlyList<Event> Events, IReadOnlyList<GoogleTask> Tasks);

    private sealed record FetchResult(List<AgendaItem> Items, List<string> FailedSources, List<Guid> UnavailableOwners);

    public async Task<IReadOnlyList<SourceDto>> GetSourcesAsync(Guid userId, bool canReadTasks, CancellationToken ct)
    {
        using var clients = await clientFactory.CreateAsync(userId, ct);
        var sources = new List<SourceDto>();

        string? pageToken = null;
        do
        {
            var request = clients.Calendar.CalendarList.List();
            request.Fields = "nextPageToken,items(id,summary,summaryOverride,primary,backgroundColor,accessRole)";
            request.PageToken = pageToken;
            var page = await request.ExecuteAsync(ct);

            foreach (var cal in page.Items ?? [])
            {
                // freeBusyReader can't list events, only availability.
                if (cal.AccessRole == "freeBusyReader") continue;
                sources.Add(new SourceDto(
                    SourceId.Calendar(cal.Id).ToString(),
                    SourceKind.Calendar,
                    cal.SummaryOverride ?? cal.Summary ?? cal.Id,
                    cal.Primary == true,
                    cal.BackgroundColor));
            }

            pageToken = page.NextPageToken;
        } while (pageToken is not null);

        if (canReadTasks)
        {
            var lists = await clients.Tasks.Tasklists.List().ExecuteAsync(ct);
            sources.AddRange((lists.Items ?? []).Select(l => new SourceDto(
                SourceId.TaskList(l.Id).ToString(), SourceKind.TaskList, l.Title, false, null)));
        }

        // Primary calendar first, then the rest by name.
        return sources
            .OrderByDescending(s => s.Primary)
            .ThenBy(s => s.Kind)
            .ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Personal agenda (users without a group): only the user's own sources, always with details.</summary>
    public async Task<AgendaResponse> GetOwnAgendaAsync(
        Guid userId, DateTimeOffset from, DateTimeOffset to, IReadOnlyCollection<SourceId> sources, CancellationToken ct)
    {
        var requests = sources.Distinct().Select(s => new SourceRequest(userId, s, SourceVisibility.Details)).ToList();
        var result = await FetchAsync(userId, requests, from, to, ct);

        // In the personal agenda there's nothing to show without the user's own connection: the front end must ask to reconnect.
        if (result.UnavailableOwners.Contains(userId))
            throw new GoogleReconnectRequiredException();

        return new AgendaResponse(result.Items, result.FailedSources);
    }

    /// <summary>
    /// Group agenda: everything each member shared, read with EACH MEMBER'S token
    /// (that's why we ask for offline access at login) and filtered by each one's privacy rules.
    /// </summary>
    public async Task<GroupAgendaResponse> GetGroupAgendaAsync(
        Guid groupId, Guid viewerId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var shares = await db.SharedSources.AsNoTracking()
            .Where(s => s.GroupId == groupId)
            .Select(s => new { s.UserId, s.Kind, s.GoogleId, s.Visibility })
            .ToListAsync(ct);

        var requests = shares
            .Select(s => new SourceRequest(s.UserId, new SourceId(s.Kind, s.GoogleId), s.Visibility))
            .ToList();

        var result = await FetchAsync(viewerId, requests, from, to, ct);
        return new GroupAgendaResponse(result.Items, result.FailedSources, result.UnavailableOwners);
    }

    private async Task<FetchResult> FetchAsync(
        Guid viewerId, IReadOnlyList<SourceRequest> requests, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var result = new FetchResult([], [], []);
        var loaded = new List<(SourceRequest Request, RawSource Data)>();
        var toFetch = new List<SourceRequest>();

        foreach (var request in requests)
        {
            if (cache.TryGetValue(CacheKey(request, from, to), out RawSource? hit) && hit is not null)
                loaded.Add((request, hit));
            else
                toFetch.Add(request);
        }

        var clients = new Dictionary<Guid, GoogleClients>();
        try
        {
            // 1) One client (one access token) per owner, SEQUENTIALLY: GoogleTokenService uses the DbContext,
            //    which isn't thread-safe. Owners with an expired connection become "unavailable" without breaking the others.
            foreach (var ownerId in toFetch.Select(r => r.OwnerId).Distinct())
            {
                try
                {
                    clients[ownerId] = await clientFactory.CreateAsync(ownerId, ct);
                }
                catch (GoogleReconnectRequiredException)
                {
                    result.UnavailableOwners.Add(ownerId);
                }
            }

            // 2) Google calls in PARALLEL: total time is the slowest calendar, not the sum.
            var fetched = await Task.WhenAll(toFetch.Where(r => clients.ContainsKey(r.OwnerId)).Select(async request =>
            {
                try
                {
                    var data = await FetchRawAsync(clients[request.OwnerId], request.Source, from, to, ct);
                    cache.Set(CacheKey(request, from, to), data, CacheTtl);
                    return (request, data: (RawSource?)data);
                }
                catch (GoogleApiException ex)
                {
                    // One broken calendar (removed, no permission) doesn't take down the whole screen.
                    logger.LogWarning(ex, "Failed to read {Source} for user {UserId}: {Status}",
                        request.Source, request.OwnerId, ex.HttpStatusCode);
                    if (ex.HttpStatusCode == HttpStatusCode.Unauthorized)
                        tokens.Invalidate(request.OwnerId);
                    return (request, data: (RawSource?)null);
                }
            }));

            foreach (var (request, data) in fetched)
            {
                if (data is not null) loaded.Add((request, data));
                else result.FailedSources.Add(request.Source.ToString());
            }
        }
        finally
        {
            foreach (var client in clients.Values) client.Dispose();
        }

        // 3) Privacy rules applied PER VIEWER.
        foreach (var (request, data) in loaded)
            result.Items.AddRange(Map(request, data, viewerId));

        return result;
    }

    private static IEnumerable<AgendaItem> Map(SourceRequest request, RawSource data, Guid viewerId)
    {
        var viewerIsOwner = request.OwnerId == viewerId;

        foreach (var ev in data.Events)
        {
            var item = AgendaMapper.FromEvent(ev, request.Source, request.Visibility, viewerIsOwner);
            if (item is not null) yield return WithOwner(item, request.OwnerId);
        }

        // A task isn't a time block: in a list shared as "busy only" there's nothing to show without revealing the title.
        if (!viewerIsOwner && request.Visibility == SourceVisibility.BusyOnly) yield break;

        foreach (var task in data.Tasks)
        {
            var item = AgendaMapper.FromTask(task, request.Source);
            if (item is not null) yield return WithOwner(item, request.OwnerId);
        }
    }

    /// <summary>
    /// The owner is prefixed to the id: if both people attend the same event (e.g. one invited the other),
    /// it comes from both calendars with the same Google id, and FullCalendar requires unique ids.
    /// </summary>
    private static AgendaItem WithOwner(AgendaItem item, Guid ownerId) =>
        item with { Id = $"{ownerId:N}/{item.Id}", MemberId = ownerId };

    private static async Task<RawSource> FetchRawAsync(
        GoogleClients clients, SourceId source, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        source.Kind == SourceKind.Calendar
            ? new RawSource(await FetchEventsAsync(clients, source, from, to, ct), [])
            : new RawSource([], await FetchTasksAsync(clients, source, from, to, ct));

    private static async Task<IReadOnlyList<Event>> FetchEventsAsync(
        GoogleClients clients, SourceId source, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var events = new List<Event>();
        string? pageToken = null;
        do
        {
            var request = clients.Calendar.Events.List(source.GoogleId);
            request.TimeMinDateTimeOffset = from;
            request.TimeMaxDateTimeOffset = to;
            // Expands recurring events into individual occurrences (otherwise only the recurrence "rule" comes back).
            request.SingleEvents = true;
            request.MaxResults = 2500;
            // Asking only for the fields we use makes the response smaller and faster.
            request.Fields = EventFields;
            request.PageToken = pageToken;

            var page = await request.ExecuteAsync(ct);
            events.AddRange(page.Items ?? []);
            pageToken = page.NextPageToken;
        } while (pageToken is not null);

        return events;
    }

    private static async Task<IReadOnlyList<GoogleTask>> FetchTasksAsync(
        GoogleClients clients, SourceId source, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var tasks = new List<GoogleTask>();
        string? pageToken = null;
        do
        {
            var request = clients.Tasks.Tasks.List(source.GoogleId);
            // A task's "due" is midnight UTC of the date. We widen the range by 1 day so tasks at the edge
            // aren't lost because of the time zone (midnight in Brasília = 03:00 UTC).
            request.DueMin = ToRfc3339(from.AddDays(-1));
            request.DueMax = ToRfc3339(to.AddDays(1));
            request.ShowCompleted = false;
            request.ShowHidden = false;
            request.MaxResults = 100;
            request.Fields = TaskFields;
            request.PageToken = pageToken;

            var page = await request.ExecuteAsync(ct);
            tasks.AddRange(page.Items ?? []);
            pageToken = page.NextPageToken;
        } while (pageToken is not null);

        return tasks;
    }

    private static string ToRfc3339(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    // The cache is keyed by OWNER + source + range (raw data), so it serves any viewer.
    private static string CacheKey(SourceRequest request, DateTimeOffset from, DateTimeOffset to) =>
        $"agenda-raw:{request.OwnerId}:{request.Source}:{from.UtcTicks}:{to.UtcTicks}";
}
