using System.Globalization;
using System.Net;
using Agenda.Api.GoogleApi;
using Google;
using Microsoft.Extensions.Caching.Memory;

namespace Agenda.Api.Schedule;

/// <summary>
/// Lê eventos e tarefas direto do Google (sem salvar no banco) com um cache curto.
/// Por que não sincronizar para o banco já? Com poucos usuários, ler sob demanda é mais simples,
/// guarda menos dado pessoal e nunca fica desatualizado. Sincronização (syncToken + webhooks) é para escala.
/// </summary>
public sealed class AgendaService(
    GoogleClientFactory clientFactory,
    GoogleTokenService tokens,
    IMemoryCache cache,
    ILogger<AgendaService> logger)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(2);

    private const string EventFields =
        "nextPageToken,items(id,status,summary,location,start,end,visibility,transparency,eventType,attendees(self,responseStatus))";

    private const string TaskFields = "nextPageToken,items(id,title,due,status,deleted,hidden)";

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
                // freeBusyReader não permite listar eventos, só disponibilidade.
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
                SourceId.TaskList(l.Id).ToString(), SourceKind.TaskList, l.Title ?? "Tarefas", false, null)));
        }

        // Agenda principal primeiro, depois as demais por nome.
        return sources
            .OrderByDescending(s => s.Primary)
            .ThenBy(s => s.Kind)
            .ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<AgendaResponse> GetOwnAgendaAsync(
        Guid userId, DateTimeOffset from, DateTimeOffset to, IReadOnlyCollection<SourceId> sources, CancellationToken ct)
    {
        var items = new List<AgendaItem>();
        var failed = new List<string>();
        var toFetch = new List<SourceId>();

        foreach (var source in sources.Distinct())
        {
            if (cache.TryGetValue(CacheKey(userId, source, from, to), out IReadOnlyList<AgendaItem>? hit) && hit is not null)
                items.AddRange(hit);
            else
                toFetch.Add(source);
        }

        if (toFetch.Count == 0)
            return new AgendaResponse(items, failed);

        using var clients = await clientFactory.CreateAsync(userId, ct);

        // Todas as agendas em paralelo: o tempo total vira o da agenda mais lenta, não a soma.
        var results = await Task.WhenAll(toFetch.Select(async source =>
        {
            try
            {
                var fetched = source.Kind == SourceKind.Calendar
                    ? await FetchEventsAsync(clients, source, from, to, ct)
                    : await FetchTasksAsync(clients, source, from, to, ct);
                cache.Set(CacheKey(userId, source, from, to), fetched, CacheTtl);
                return (source, items: fetched, ok: true);
            }
            catch (GoogleApiException ex)
            {
                // Uma agenda com problema (removida, sem permissão) não derruba a tela inteira.
                logger.LogWarning(ex, "Falha ao ler {Source} do usuário {UserId}: {Status}", source, userId, ex.HttpStatusCode);
                if (ex.HttpStatusCode == HttpStatusCode.Unauthorized)
                    tokens.Invalidate(userId);
                return (source, items: (IReadOnlyList<AgendaItem>)[], ok: false);
            }
        }));

        foreach (var result in results)
        {
            if (result.ok) items.AddRange(result.items);
            else failed.Add(result.source.ToString());
        }

        return new AgendaResponse(items, failed);
    }

    private static async Task<IReadOnlyList<AgendaItem>> FetchEventsAsync(
        GoogleClients clients, SourceId source, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var items = new List<AgendaItem>();
        string? pageToken = null;
        do
        {
            var request = clients.Calendar.Events.List(source.GoogleId);
            request.TimeMinDateTimeOffset = from;
            request.TimeMaxDateTimeOffset = to;
            // Expande eventos recorrentes em ocorrências individuais (senão viria só a "regra" da recorrência).
            request.SingleEvents = true;
            request.MaxResults = 2500;
            // Pedir só os campos usados deixa a resposta menor e mais rápida.
            request.Fields = EventFields;
            request.PageToken = pageToken;

            var page = await request.ExecuteAsync(ct);
            foreach (var ev in page.Items ?? [])
            {
                var item = AgendaMapper.FromEvent(ev, source, SourceVisibility.Details, viewerIsOwner: true);
                if (item is not null) items.Add(item);
            }

            pageToken = page.NextPageToken;
        } while (pageToken is not null);

        return items;
    }

    private static async Task<IReadOnlyList<AgendaItem>> FetchTasksAsync(
        GoogleClients clients, SourceId source, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var items = new List<AgendaItem>();
        string? pageToken = null;
        do
        {
            var request = clients.Tasks.Tasks.List(source.GoogleId);
            // O "due" das tarefas é meia-noite UTC da data. Alargamos 1 dia para não perder tarefas
            // na borda por causa do fuso (meia-noite em Brasília = 03:00 UTC).
            request.DueMin = ToRfc3339(from.AddDays(-1));
            request.DueMax = ToRfc3339(to.AddDays(1));
            request.ShowCompleted = false;
            request.ShowHidden = false;
            request.MaxResults = 100;
            request.Fields = TaskFields;
            request.PageToken = pageToken;

            var page = await request.ExecuteAsync(ct);
            foreach (var task in page.Items ?? [])
            {
                var item = AgendaMapper.FromTask(task, source);
                if (item is not null) items.Add(item);
            }

            pageToken = page.NextPageToken;
        } while (pageToken is not null);

        return items;
    }

    private static string ToRfc3339(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string CacheKey(Guid userId, SourceId source, DateTimeOffset from, DateTimeOffset to) =>
        $"agenda:{userId}:{source}:{from.UtcTicks}:{to.UtcTicks}";
}
