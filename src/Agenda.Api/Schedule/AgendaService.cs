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
/// Lê eventos e tarefas direto do Google (sem salvar no banco) com um cache curto.
/// Por que não sincronizar para o banco já? Com poucos usuários, ler sob demanda é mais simples,
/// guarda menos dado pessoal e nunca fica desatualizado. Sincronização (syncToken + webhooks) é para escala.
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

    /// <summary>Uma fonte a ler: de quem é, qual agenda e com quanto detalhe ela foi compartilhada.</summary>
    private sealed record SourceRequest(Guid OwnerId, SourceId Source, SourceVisibility Visibility);

    /// <summary>Resposta crua do Google. É ISSO que vai para o cache, não o resultado filtrado.</summary>
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

    /// <summary>Agenda pessoal (quem não está em grupo): só as fontes do próprio usuário, sempre com detalhes.</summary>
    public async Task<AgendaResponse> GetOwnAgendaAsync(
        Guid userId, DateTimeOffset from, DateTimeOffset to, IReadOnlyCollection<SourceId> sources, CancellationToken ct)
    {
        var requests = sources.Distinct().Select(s => new SourceRequest(userId, s, SourceVisibility.Details)).ToList();
        var result = await FetchAsync(userId, requests, from, to, ct);

        // Na agenda pessoal não há o que mostrar sem a própria conexão: o front precisa pedir reconexão.
        if (result.UnavailableOwners.Contains(userId))
            throw new GoogleReconnectRequiredException();

        return new AgendaResponse(result.Items, result.FailedSources);
    }

    /// <summary>
    /// Agenda do grupo: tudo o que cada membro compartilhou, lido com o token DE CADA MEMBRO
    /// (por isso pedimos acesso offline no login) e filtrado pelas regras de privacidade de cada um.
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
            // 1) Um cliente (um access token) por dono, em SEQUÊNCIA: o GoogleTokenService usa o DbContext,
            //    que não é thread-safe. Quem estiver com a conexão expirada vira "indisponível", sem derrubar os outros.
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

            // 2) As chamadas ao Google em PARALELO: o tempo total vira o da agenda mais lenta, não a soma.
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
                    // Uma agenda com problema (removida, sem permissão) não derruba a tela inteira.
                    logger.LogWarning(ex, "Falha ao ler {Source} do usuário {UserId}: {Status}",
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

        // 3) Regras de privacidade aplicadas POR QUEM ESTÁ VENDO.
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

        // Tarefa não é bloco de tempo: numa lista compartilhada como "só ocupado" não há o que mostrar sem revelar o título.
        if (!viewerIsOwner && request.Visibility == SourceVisibility.BusyOnly) yield break;

        foreach (var task in data.Tasks)
        {
            var item = AgendaMapper.FromTask(task, request.Source);
            if (item is not null) yield return WithOwner(item, request.OwnerId);
        }
    }

    /// <summary>
    /// O id ganha o dono como prefixo: se os dois estão no mesmo evento (ex.: um convidou o outro),
    /// ele vem nas duas agendas com o mesmo id do Google e o FullCalendar exige ids únicos.
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
            // Expande eventos recorrentes em ocorrências individuais (senão viria só a "regra" da recorrência).
            request.SingleEvents = true;
            request.MaxResults = 2500;
            // Pedir só os campos usados deixa a resposta menor e mais rápida.
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
            tasks.AddRange(page.Items ?? []);
            pageToken = page.NextPageToken;
        } while (pageToken is not null);

        return tasks;
    }

    private static string ToRfc3339(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    // O cache é por DONO + fonte + intervalo (dado cru), então serve a qualquer pessoa que esteja vendo.
    private static string CacheKey(SourceRequest request, DateTimeOffset from, DateTimeOffset to) =>
        $"agenda-raw:{request.OwnerId}:{request.Source}:{from.UtcTicks}:{to.UtcTicks}";
}
