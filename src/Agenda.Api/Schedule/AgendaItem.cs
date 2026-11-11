namespace Agenda.Api.Schedule;

public enum AgendaItemKind
{
    Event,
    Task,

    /// <summary>Bloco "Ocupado", sem título: evento privado ou agenda compartilhada só como disponibilidade.</summary>
    Busy,
}

public enum SourceVisibility
{
    Details,
    BusyOnly,
}

public enum SourceKind
{
    Calendar,
    TaskList,
}

/// <summary>
/// Formato normalizado que o front recebe. O front não conhece o Google: se um dia
/// entrar Outlook, só o backend muda.
/// </summary>
/// <param name="Start">Eventos com hora: ISO 8601 em UTC. Dia inteiro: "yyyy-MM-dd".</param>
/// <param name="End">Exclusivo, como no Google e no FullCalendar (evento do dia 10 termina em "11").</param>
/// <param name="MemberId">Dono do item na agenda do grupo (define a cor). Nulo na agenda pessoal.</param>
public sealed record AgendaItem(
    string Id,
    string SourceId,
    AgendaItemKind Kind,
    string? Title,
    string Start,
    string End,
    bool AllDay,
    string? Location,
    Guid? MemberId = null);

public sealed record AgendaResponse(IReadOnlyList<AgendaItem> Items, IReadOnlyList<string> FailedSources);

/// <param name="UnavailableMembers">Membros cuja conexão com o Google expirou: a agenda deles não pôde ser lida.</param>
public sealed record GroupAgendaResponse(
    IReadOnlyList<AgendaItem> Items,
    IReadOnlyList<string> FailedSources,
    IReadOnlyList<Guid> UnavailableMembers);

public static class AgendaRange
{
    public static readonly TimeSpan Max = TimeSpan.FromDays(62);

    public static Dictionary<string, string[]> Validate(DateTimeOffset from, DateTimeOffset to)
    {
        var errors = new Dictionary<string, string[]>();
        if (to <= from) errors["to"] = ["'to' deve ser maior que 'from'."];
        else if (to - from > Max) errors["to"] = [$"O intervalo máximo é de {Max.TotalDays} dias."];
        return errors;
    }
}

public sealed record SourceDto(string Id, SourceKind Kind, string Name, bool Primary, string? GoogleColor);

/// <summary>
/// Identificador de fonte no formato "cal:{id}" ou "tasks:{id}".
/// O prefixo diz qual API do Google chamar (Calendar ou Tasks).
/// </summary>
public readonly record struct SourceId(SourceKind Kind, string GoogleId)
{
    private const string CalendarPrefix = "cal:";
    private const string TasksPrefix = "tasks:";

    public override string ToString() =>
        (Kind == SourceKind.Calendar ? CalendarPrefix : TasksPrefix) + GoogleId;

    public static SourceId Calendar(string googleId) => new(SourceKind.Calendar, googleId);

    public static SourceId TaskList(string googleId) => new(SourceKind.TaskList, googleId);

    public static bool TryParse(string? value, out SourceId sourceId)
    {
        sourceId = default;
        if (string.IsNullOrWhiteSpace(value)) return false;

        if (value.StartsWith(CalendarPrefix, StringComparison.Ordinal) && value.Length > CalendarPrefix.Length)
        {
            sourceId = Calendar(value[CalendarPrefix.Length..]);
            return true;
        }

        if (value.StartsWith(TasksPrefix, StringComparison.Ordinal) && value.Length > TasksPrefix.Length)
        {
            sourceId = TaskList(value[TasksPrefix.Length..]);
            return true;
        }

        return false;
    }
}
