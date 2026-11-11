using System.Globalization;
using Google.Apis.Calendar.v3.Data;
using GoogleTask = Google.Apis.Tasks.v1.Data.Task;

namespace Agenda.Api.Schedule;

/// <summary>
/// Regras de negócio de "o que aparece e como". Funções puras (sem I/O),
/// por isso são o lugar mais fácil e mais importante de testar.
/// </summary>
public static class AgendaMapper
{
    private const string DateFormat = "yyyy-MM-dd";
    private const string UtcFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    /// <param name="viewerIsOwner">O dono sempre vê tudo da própria agenda; as restrições valem para os outros membros.</param>
    public static AgendaItem? FromEvent(Event ev, SourceId source, SourceVisibility visibility, bool viewerIsOwner)
    {
        if (ev.Status == "cancelled") return null;

        // "Trabalhando de casa/escritório" vira um evento no Google, mas não é compromisso.
        if (ev.EventType == "workingLocation") return null;

        // Convite recusado não ocupa ninguém.
        if (ev.Attendees?.Any(a => a.Self == true && a.ResponseStatus == "declined") == true) return null;

        var isPrivate = ev.Visibility is "private" or "confidential";
        var showDetails = viewerIsOwner || (visibility == SourceVisibility.Details && !isPrivate);

        // Evento marcado como "Disponível" no Google: se não podemos mostrar os detalhes,
        // um bloco "Ocupado" mentiria sobre a disponibilidade. Então some.
        if (!showDetails && ev.Transparency == "transparent") return null;

        if (!TryGetRange(ev, out var start, out var end, out var allDay)) return null;

        return new AgendaItem(
            Id: $"{source}/{ev.Id}",
            SourceId: source.ToString(),
            Kind: showDetails ? AgendaItemKind.Event : AgendaItemKind.Busy,
            Title: showDetails ? (string.IsNullOrWhiteSpace(ev.Summary) ? "(sem título)" : ev.Summary) : null,
            Start: start,
            End: end,
            AllDay: allDay,
            Location: showDetails ? ev.Location : null);
    }

    /// <summary>
    /// A API do Google Tarefas guarda só a DATA de vencimento (a hora é descartada),
    /// então toda tarefa vira um item de dia inteiro.
    /// </summary>
    public static AgendaItem? FromTask(GoogleTask task, SourceId source)
    {
        if (task.Deleted == true || task.Hidden == true || task.Status == "completed") return null;

        // "due" vem como "2026-11-12T00:00:00.000Z". Pegamos só a data, sem converter fuso:
        // converter para o horário de Brasília jogaria a tarefa para o dia anterior (21h do dia 11).
        if (task.Due is not { Length: >= 10 } due) return null;
        if (!DateOnly.TryParseExact(due[..10], DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return null;

        return new AgendaItem(
            Id: $"{source}/{task.Id}",
            SourceId: source.ToString(),
            Kind: AgendaItemKind.Task,
            Title: string.IsNullOrWhiteSpace(task.Title) ? "(tarefa sem título)" : task.Title,
            Start: date.ToString(DateFormat, CultureInfo.InvariantCulture),
            End: date.AddDays(1).ToString(DateFormat, CultureInfo.InvariantCulture),
            AllDay: true,
            Location: null);
    }

    private static bool TryGetRange(Event ev, out string start, out string end, out bool allDay)
    {
        // Dia inteiro: o Google preenche "date" (e não "dateTime"). O fim é exclusivo.
        if (ev.Start?.Date is { Length: > 0 } startDate)
        {
            start = startDate;
            end = ev.End?.Date is { Length: > 0 } endDate
                ? endDate
                : DateOnly.ParseExact(startDate, DateFormat, CultureInfo.InvariantCulture).AddDays(1)
                    .ToString(DateFormat, CultureInfo.InvariantCulture);
            allDay = true;
            return true;
        }

        // Com hora: normalizamos para UTC. Quem converte para o fuso de quem está vendo é o front.
        if (ev.Start?.DateTimeDateTimeOffset is { } s && ev.End?.DateTimeDateTimeOffset is { } e)
        {
            start = s.ToUniversalTime().ToString(UtcFormat, CultureInfo.InvariantCulture);
            end = e.ToUniversalTime().ToString(UtcFormat, CultureInfo.InvariantCulture);
            allDay = false;
            return true;
        }

        start = end = "";
        allDay = false;
        return false;
    }
}
