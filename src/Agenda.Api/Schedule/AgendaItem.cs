namespace Agenda.Api.Schedule;

public enum AgendaItemKind
{
    Event,
    Task,

    /// <summary>"Busy" block without a title: a private event or a calendar shared as availability only.</summary>
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
/// Normalized shape sent to the front end. The front end doesn't know about Google: if Outlook
/// ever comes in, only the back end changes. It also carries no UI text (the front end translates).
/// </summary>
/// <param name="Title">Null for "busy" items and for events/tasks without a title (the front end shows a localized placeholder).</param>
/// <param name="Start">Timed events: ISO 8601 in UTC. All-day: "yyyy-MM-dd".</param>
/// <param name="End">Exclusive, as in Google and FullCalendar (an event on the 10th ends on "11").</param>
/// <param name="MemberId">Owner of the item in the group calendar (defines the color).</param>
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

/// <param name="UnavailableMembers">Members whose Google connection expired: their calendars could not be read.</param>
public sealed record GroupAgendaResponse(
    IReadOnlyList<AgendaItem> Items,
    IReadOnlyList<string> FailedSources,
    IReadOnlyList<Guid> UnavailableMembers);

/// <param name="Name">Null when Google has no name for it (the front end shows a localized placeholder).</param>
public sealed record SourceDto(string Id, SourceKind Kind, string? Name, bool Primary, string? GoogleColor);

public static class AgendaRange
{
    public static readonly TimeSpan Max = TimeSpan.FromDays(62);

    public static Dictionary<string, string[]> Validate(DateTimeOffset from, DateTimeOffset to)
    {
        var errors = new Dictionary<string, string[]>();
        if (to <= from) errors["to"] = ["'to' must be greater than 'from'."];
        else if (to - from > Max) errors["to"] = [$"The maximum range is {Max.TotalDays} days."];
        return errors;
    }
}

/// <summary>
/// Source identifier in the form "cal:{id}" or "tasks:{id}".
/// The prefix tells which Google API to call (Calendar or Tasks).
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
